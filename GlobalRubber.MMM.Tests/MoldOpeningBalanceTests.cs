using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Rules;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Registering a mold that is already in production (opening balance): usage starts at the opening usage, the PM cycle is
/// anchored on the last PM (or the last whole interval), and the mold is PM-evaluated in the insert's transaction.
/// FixedClock today = 2026-03-01.
/// </summary>
public class MoldOpeningBalanceTests
{
    private sealed record Sut(MoldService Service, InMemoryMoldRepository Molds, InMemoryMoldPmRepository Pms, InMemoryNotificationRepository Notifications, RecordingAuditLog Audit);

    private static Sut Create()
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var products = new InMemoryProductRepository(ProductTestData.Products());
        var moldList = MoldTestData.Molds();
        var molds = new InMemoryMoldRepository(moldList, products, employees);
        var pms = new InMemoryMoldPmRepository(moldList);
        var notifications = new InMemoryNotificationRepository();
        var audit = new RecordingAuditLog();
        var evaluator = MoldPmTestFactory.Evaluator(pms, notifications, new FixedClock(), audit);
        var service = new MoldService(molds, evaluator, products, employees, users, new FixedClock(), audit, NullLogger<MoldService>.Instance);
        return new Sut(service, molds, pms, notifications, audit);
    }

    private static CreateMoldRequest Existing(
        int? opening = 320000, int? lastPm = null, DateOnly? date = null, int? interval = 50000, int? pmWarning = null,
        DateOnly? commissioned = null, bool noDate = false) => new()
    {
        MoldName = "Running Mold X", ProductId = 2, MoldType = "Injection", CavityCount = 2, SerialNumber = "RM-X1",
        CommissionDate = commissioned ?? new DateOnly(2023, 1, 1), MaximumShots = 500000, WarningShots = 450000, ReplacementShots = 500000,
        MaintenanceFrequencyShots = interval, PmWarningShots = pmWarning, Status = "Available",
        OpeningUsageShots = opening, UsageAtLastPmShots = lastPm,
        OpeningBalanceDate = noDate ? null : date ?? new DateOnly(2026, 2, 28),
    };

    [Theory]
    [InlineData(320000, null, 50000, 300000)] // last whole interval
    [InlineData(300000, null, 50000, 300000)] // exactly on an interval
    [InlineData(49999, null, 50000, 0)]
    [InlineData(320000, 280000, 50000, 280000)] // last PM known
    [InlineData(320000, null, null, 0)]         // PM disabled
    public void OpeningCycleStart_FollowsTheLastPm_OrTheLastWholeInterval(int opening, int? lastPm, int? interval, int expected) =>
        Assert.Equal(expected, MoldPmRules.OpeningCycleStart(opening, lastPm, interval));

    [Fact]
    public async Task Create_WithoutOpeningBalance_StartsAtZero_AsBefore()
    {
        var sut = Create();

        var dto = await sut.Service.CreateAsync(Existing(opening: null, noDate: true), 1, null, CancellationToken.None);

        Assert.Equal(0, dto.CurrentUsageShots);
        Assert.Equal(0, dto.PmCycleStartShots);
        Assert.Empty(sut.Pms.Pms);
    }

    [Fact]
    public async Task Create_WithOpeningUsage_NoLastPm_IsNotFlaggedOverdue()
    {
        var sut = Create();

        var dto = await sut.Service.CreateAsync(Existing(), 1, null, CancellationToken.None);

        Assert.Equal(320000, dto.CurrentUsageShots);
        Assert.Equal(300000, dto.PmCycleStartShots);
        Assert.Equal(350000, dto.PmNextThresholdShots);
        Assert.Equal(30000, dto.PmRemainingShots);
        Assert.Equal(64, dto.LifeUsedPercent);
        Assert.Empty(sut.Pms.Pms); // no fake PM for cycles long past
        Assert.Equal(320000, sut.Molds.Stored(dto.MoldId).CurrentUsageShots);
        Assert.Equal(300000, sut.Molds.Stored(dto.MoldId).PmCycleStartShots);
    }

    [Fact]
    public async Task Create_WithLastPm_AnchorsTheCycle_AndRaisesTheWarningWhenInsideTheMargin()
    {
        var sut = Create();

        var dto = await sut.Service.CreateAsync(Existing(lastPm: 280000, pmWarning: 15000), 1, null, CancellationToken.None);

        Assert.Equal(280000, dto.PmCycleStartShots);
        Assert.Equal(330000, dto.PmNextThresholdShots);
        Assert.Equal(10000, dto.PmRemainingShots);
        Assert.Empty(sut.Pms.Pms);
        Assert.Single(sut.Notifications.Items, n => n.NotificationType == NotificationTypes.MoldPmWarning);
    }

    [Fact]
    public async Task Create_WhenGenuinelyDue_CreatesOneAutomaticPm()
    {
        var sut = Create();

        var dto = await sut.Service.CreateAsync(Existing(opening: 400000, lastPm: 280000), 1, null, CancellationToken.None);

        var pm = Assert.Single(sut.Pms.Pms);
        Assert.Equal(dto.MoldId, pm.MoldId);
        Assert.Equal(330000, pm.ThresholdShots);
        Assert.Equal(400000, pm.MoldUsageAtService);
        Assert.Equal(MoldPmStatus.Scheduled, pm.Status);
    }

    [Fact]
    public async Task Create_WithOpeningBalance_AuditsTheOpeningFigures()
    {
        var sut = Create();

        var dto = await sut.Service.CreateAsync(Existing(lastPm: 280000), 1, null, CancellationToken.None);

        var entry = Assert.Single(sut.Audit.Entries, e => e.Action == "MoldCreated");
        Assert.Equal(dto.MoldCode, entry.RecordRef);
        Assert.Contains(entry.Details, d => d.FieldName == "opening_usage_shots" && d.NewValue == "320000");
        Assert.Contains(entry.Details, d => d.FieldName == "opening_balance_date" && d.NewValue == "2026-02-28");
        Assert.Contains(entry.Details, d => d.FieldName == "usage_at_last_pm_shots" && d.NewValue == "280000");
        Assert.Contains(entry.Details, d => d.FieldName == "pm_cycle_start_shots" && d.NewValue == "280000");
    }

    public static TheoryData<CreateMoldRequest, string> Invalid => new()
    {
        { Existing(opening: -1), "Opening usage (shots) cannot be negative." },
        { Existing(noDate: true), "Opening balance date is required for a mold already in production." },
        { Existing(date: new DateOnly(2026, 3, 2)), "Opening balance date cannot be in the future." },
        { Existing(date: new DateOnly(2022, 12, 31)), "Opening balance date cannot be before the commission date." },
        { Existing(lastPm: 320001), "Usage at last PM (shots) cannot be more than the opening usage." },
        { Existing(lastPm: -5), "Usage at last PM (shots) cannot be negative." },
        { Existing(lastPm: 280000, interval: null), "Usage at last PM (shots) needs a maintenance frequency (shots)." },
        { Existing(opening: null, lastPm: 1000), "Opening usage (shots) is required when an opening balance date or usage at last PM is entered." },
        { Existing(opening: null), "Opening usage (shots) is required when an opening balance date or usage at last PM is entered." },
    };

    [Theory]
    [MemberData(nameof(Invalid))]
    public async Task Create_RejectsAnInvalidOpeningBalance(CreateMoldRequest request, string message)
    {
        var sut = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => sut.Service.CreateAsync(request, 1, null, CancellationToken.None));

        Assert.Contains(message, ex.Errors);
        Assert.Equal(0, sut.Molds.AddCalls);
    }

    [Fact]
    public async Task Update_IgnoresTheOpeningBalanceFields()
    {
        var sut = Create();
        var created = await sut.Service.CreateAsync(Existing(), 1, null, CancellationToken.None);
        var stored = sut.Molds.Stored(created.MoldId);

        var updated = await sut.Service.UpdateAsync(created.MoldId, new UpdateMoldRequest
        {
            MoldName = stored.MoldName, ProductId = stored.ProductId, MoldType = stored.MoldType, CavityCount = stored.CavityCount,
            SerialNumber = stored.SerialNumber, CommissionDate = stored.CommissionDate, MaximumShots = stored.MaximumShots,
            WarningShots = stored.WarningShots, ReplacementShots = stored.ReplacementShots,
            MaintenanceFrequencyShots = stored.MaintenanceFrequencyShots, Status = stored.Status,
            OpeningUsageShots = 1, UsageAtLastPmShots = 1, OpeningBalanceDate = new DateOnly(2030, 1, 1),
            RowVersion = Convert.ToBase64String(stored.RowVersion),
        }, 1, null, CancellationToken.None);

        Assert.Equal(320000, updated.CurrentUsageShots);
        Assert.Equal(300000, updated.PmCycleStartShots);
    }
}
