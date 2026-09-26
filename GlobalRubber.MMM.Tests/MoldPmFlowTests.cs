using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The whole usage-based Mold PM flow through the REAL services (ProductionEntryService, MoldService, MoldPmService,
/// MoldPmEvaluator) over shared in-memory fakes. Mold 10 "MLD-0010": interval 100,000, warning margin 10,000, cycle from
/// 500,000, usage 500,000, life limits far above (so BR-10 never interferes).
/// </summary>
public class MoldPmFlowTests
{
    private const int MoldId = 10;

    private sealed class World
    {
        public required List<Mold> Molds { get; init; }
        public required ProductionEntryService Production { get; init; }
        public required MoldService MoldMaster { get; init; }
        public required MoldPmService MoldPm { get; init; }
        public required InMemoryMoldPmRepository Pms { get; init; }
        public required InMemoryNotificationRepository Notifications { get; init; }
        public required RecordingAuditLog Audit { get; init; }
        public required MovableClock Clock { get; init; }
        public required InMemoryProductionEntryRepository Entries { get; init; }

        public Mold Mold => Molds.Single(m => m.MoldId == MoldId);

        public Task<ProductionEntryDto> ProduceAsync(int qty) => Production.CreateAsync(new CreateProductionEntryRequest
        {
            EntryDate = Clock.Today, Shift = "Shift A", MachineId = 1, ProductId = 1, MoldId = MoldId, ProductionQty = qty, RejectedQty = 0,
        }, 1, null, CancellationToken.None);

        public async Task ProduceToAsync(int usage) => await ProduceAsync(usage - Mold.CurrentUsageShots);

        public MoldPm OpenPm => Pms.Pms.Single(p => p.MoldId == MoldId && p.Status != MoldPmStatus.Completed);

        public int Warnings => Notifications.Items.Count(n => n.NotificationType == NotificationTypes.MoldPmWarning);
        public int DueNotices => Notifications.Items.Count(n => n.NotificationType == NotificationTypes.MoldPmDue);

        public async Task<MoldUsageDto> UsageAsync() => (await MoldPm.GetMoldUsageAsync(CancellationToken.None)).Single(u => u.MoldId == MoldId);

        public Task<MoldPmDto> CompleteOpenAsync(string by = "Ravi (maintenance)") => MoldPm.CompleteAsync(OpenPm.MoldPmId, new CompleteMoldPmRequest
        {
            MaintenanceBy = by, Remarks = "Cavities cleaned", RowVersion = Convert.ToBase64String(OpenPm.RowVersion),
        }, 2, null, CancellationToken.None);
    }

    private static World Create(Action<Mold>? configure = null, Func<InMemoryMoldPmRepository, IMoldPmRepository>? wrapPms = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machineList = MachineTestData.Machines();
        var productList = ProductTestData.Products();

        var mold = new Mold
        {
            MoldId = MoldId, MoldCode = "MLD-0010", MoldName = "Seal Mold 10", ProductId = 1, MoldType = "Compression", CavityCount = 1,
            MaximumShots = 5_000_000, WarningShots = 4_000_000, ReplacementShots = 4_500_000,
            MaintenanceFrequencyShots = 100_000, PmWarningShots = 10_000, PmCycleStartShots = 500_000, CurrentUsageShots = 500_000,
            Status = MoldStatus.InProduction, CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        };
        configure?.Invoke(mold);
        var moldList = new List<Mold> { mold };

        var machines = new InMemoryMachineRepository(machineList, departments, employees);
        var products = new InMemoryProductRepository(productList);
        var molds = new InMemoryMoldRepository(moldList, products, employees);
        var entries = new InMemoryProductionEntryRepository(moldList, machineList, productList);
        var pms = new InMemoryMoldPmRepository(moldList);
        var notifications = new InMemoryNotificationRepository();
        var audit = new RecordingAuditLog();
        var clock = new MovableClock();
        var evaluator = MoldPmTestFactory.Evaluator(wrapPms?.Invoke(pms) ?? pms, notifications, clock, audit);

        return new World
        {
            Molds = moldList,
            Production = new ProductionEntryService(entries, machines, products, molds, evaluator, users, clock, audit, NullLogger<ProductionEntryService>.Instance),
            MoldMaster = new MoldService(molds, evaluator, products, employees, users, clock, audit, NullLogger<MoldService>.Instance),
            MoldPm = new MoldPmService(pms, evaluator, users, clock, audit, NullLogger<MoldPmService>.Instance),
            Pms = pms,
            Notifications = notifications,
            Audit = audit,
            Clock = clock,
            Entries = entries,
        };
    }

    // ------------------------------------------------------------------ the required acceptance example (25)

    [Fact]
    public async Task AcceptanceExample_MLD0001_FullCycleAndTheNextOne()
    {
        var w = Create();

        await w.ProduceToAsync(580_000);
        var u = await w.UsageAsync();
        Assert.Equal((MoldPmState.Normal, 20_000L), (u.State, u.RemainingShots!.Value));
        Assert.Empty(w.Notifications.Items);

        await w.ProduceToAsync(590_000);
        u = await w.UsageAsync();
        Assert.Equal((MoldPmState.Warning, 10_000L), (u.State, u.RemainingShots!.Value));
        Assert.Equal(1, w.Warnings);
        var warning = w.Notifications.Items.Single();
        Assert.Contains("Mold MLD-0010 - Seal Mold 10 is approaching preventive maintenance. Remaining shots: 10,000.", warning.Message);
        Assert.Contains("threshold: 600,000", warning.Message);
        Assert.Equal((ModuleCodes.TrnMoldPm, (int?)null), (warning.ModuleCode, warning.CreatedBy));

        await w.ProduceToAsync(595_000);
        Assert.Equal(1, w.Warnings); // no second notification in the same cycle
        Assert.Empty(w.Pms.Pms);

        await w.ProduceToAsync(600_000);
        var pm = Assert.Single(w.Pms.Pms);
        Assert.Equal((MoldPmStatus.Scheduled, MoldPmCategory.ShotBased, 600_000, (int?)600_000, (int?)100_000, (int?)null),
            (pm.Status, pm.Category, pm.MoldUsageAtService, pm.ThresholdShots, pm.IntervalShots, pm.CreatedBy));
        Assert.Equal(w.Clock.Today, pm.ScheduledDate); // the day it became due - no invented calendar date
        Assert.Equal(1, w.DueNotices);
        Assert.Equal((MoldPmState.Due, "MPMD-0001"), ((await w.UsageAsync()).State, (await w.UsageAsync()).OpenPmNo));

        await w.ProduceToAsync(610_000);
        Assert.Single(w.Pms.Pms);   // same ONE open PM
        Assert.Equal(1, w.DueNotices);

        await w.ProduceToAsync(615_000);
        var done = await w.CompleteOpenAsync("Ravi (maintenance)");
        Assert.Equal((MoldPmStatus.Completed, "Ravi (maintenance)", (DateOnly?)w.Clock.Today, (int?)615_000),
            (done.Status, done.MaintenanceBy, done.CompletedDate, done.UsageAtCompletion));

        u = await w.UsageAsync();
        Assert.Equal((615_000, 600_000, 700_000L, 85_000L, MoldPmState.Normal, (int?)615_000),
            (u.CurrentShots, u.CycleStartShots, u.NextThresholdShots!.Value, u.RemainingShots!.Value, u.State, u.LastMaintenanceShots));
        Assert.Equal(615_000, w.Mold.CurrentUsageShots); // the counter is never reset

        await w.ProduceToAsync(690_000);
        Assert.Equal((MoldPmState.Warning, 2), ((await w.UsageAsync()).State, w.Warnings)); // the NEW cycle warns once

        await w.ProduceToAsync(700_000);
        Assert.Equal(2, w.Pms.Pms.Count);
        Assert.Equal((int?)700_000, w.OpenPm.ThresholdShots);
        Assert.Equal(2, w.DueNotices);
    }

    // ------------------------------------------------------------------ once-only guarantees

    [Fact]
    public async Task ManySmallUpdatesInsideTheWarningMargin_OneWarningOnly()
    {
        var w = Create();
        for (var usage = 590_000; usage < 600_000; usage += 1_000)
        {
            await w.ProduceToAsync(usage);
        }

        Assert.Equal(1, w.Warnings);
        Assert.Single(w.Audit.Entries, e => e.Action == MoldPmAuditNames.WarningRaised);
    }

    [Fact]
    public async Task MissedMaintenance_OneOpenPm_NoBacklog_EvenPastTheNextThreshold()
    {
        var w = Create();
        foreach (var usage in new[] { 600_000, 610_000, 620_000, 650_000, 710_000, 820_000 })
        {
            await w.ProduceToAsync(usage);
        }

        Assert.Single(w.Pms.Pms);
        Assert.Equal(1, w.DueNotices);

        await w.CompleteOpenAsync();
        var u = await w.UsageAsync();
        Assert.Equal((800_000, 900_000L, 80_000L), (u.CycleStartShots, u.NextThresholdShots!.Value, u.RemainingShots!.Value));
        Assert.Single(w.Pms.Pms); // completion never creates a backlog PM either
    }

    [Fact]
    public async Task OneJumpOverTheWarningAndTheThreshold_CreatesThePm_WithoutAWarning()
    {
        var w = Create();
        await w.ProduceToAsync(605_000);

        Assert.Single(w.Pms.Pms);
        Assert.Equal((0, 1), (w.Warnings, w.DueNotices));
    }

    [Fact]
    public async Task CompletedLate_DueAt600k_CompletedAt615k_NextThresholdIs700k_Not715k()
    {
        var w = Create();
        await w.ProduceToAsync(600_000);
        await w.ProduceToAsync(615_000);
        await w.CompleteOpenAsync();

        Assert.Equal(700_000L, (await w.UsageAsync()).NextThresholdShots);
        var audit = w.Audit.Entries.Single(e => e.Action == MoldPmAuditNames.Completed);
        Assert.Contains(audit.Details, d => d.FieldName == "next_threshold_shots" && d.NewValue == "700000");
        Assert.Contains(audit.Details, d => d.FieldName == "usage_at_completion" && d.NewValue == "615000");
        Assert.Contains(audit.Details, d => d.FieldName == "usage_at_trigger" && d.NewValue == "600000");
        Assert.Contains(audit.Details, d => d.FieldName == "pm_cycle_start_shots (MLD-0010)" && d.OldValue == "500000" && d.NewValue == "600000");
    }

    [Fact]
    public async Task CompletingDeepInsideTheNextWarningMargin_WarnsForTheNewCycleStraightAway()
    {
        var w = Create();
        await w.ProduceToAsync(600_000);
        await w.ProduceToAsync(695_000);
        await w.CompleteOpenAsync(); // cycle 600k -> next 700k, 5,000 remaining <= 10,000

        Assert.Equal(1, w.Warnings);
        Assert.Contains(w.Audit.Entries, e => e.Action == MoldPmAuditNames.WarningRaised && e.Description.Contains("completion of mold PM MPMD-0001"));
    }

    // ------------------------------------------------------------------ disabled / inactive / reactivation / configuration

    [Fact]
    public async Task PmDisabled_NoWarning_NoPm()
    {
        var w = Create(m => m.MaintenanceFrequencyShots = null);
        await w.ProduceToAsync(900_000);

        Assert.Empty(w.Pms.Pms);
        Assert.Empty(w.Notifications.Items);
        Assert.Equal(MoldPmState.NotConfigured, (await w.UsageAsync()).State);
    }

    [Fact]
    public async Task RetiredMold_ProductionStillAllowed_ButNoWarningAndNoPm()
    {
        var w = Create(m => m.Status = MoldStatus.Retired);
        await w.ProduceToAsync(595_000);
        await w.ProduceToAsync(650_000);

        Assert.Empty(w.Pms.Pms);
        Assert.Empty(w.Notifications.Items);
    }

    [Fact]
    public async Task Reactivation_OfARetiredMoldPastItsThreshold_CreatesThePmOnSave()
    {
        var w = Create(m => { m.Status = MoldStatus.Retired; m.CurrentUsageShots = 640_000; });

        await w.MoldMaster.UpdateAsync(MoldId, UpdateRequest(w.Mold, status: MoldStatus.Available), 1, null, CancellationToken.None);

        var pm = Assert.Single(w.Pms.Pms);
        Assert.Equal(((int?)600_000, 640_000), (pm.ThresholdShots, pm.MoldUsageAtService));
        Assert.Contains(w.Audit.Entries, e => e.Action == MoldPmAuditNames.AutoCreated && e.Description.Contains("the update of mold MLD-0010"));
    }

    [Fact]
    public async Task UsageCorrectionOnTheMoldMaster_IsEvaluatedToo()
    {
        var w = Create();
        await w.MoldMaster.UpdateAsync(MoldId, UpdateRequest(w.Mold, usage: 592_000), 1, null, CancellationToken.None);

        Assert.Equal(1, w.Warnings);
        Assert.Empty(w.Pms.Pms);
    }

    [Fact]
    public async Task SmallerIntervalOnTheMoldMaster_MakesItDueImmediately()
    {
        var w = Create(m => m.CurrentUsageShots = 560_000);
        await w.MoldMaster.UpdateAsync(MoldId, UpdateRequest(w.Mold, interval: 50_000, warning: 5_000), 1, null, CancellationToken.None);

        Assert.Equal((int?)550_000, Assert.Single(w.Pms.Pms).ThresholdShots);
    }

    [Theory]
    [InlineData(0, null, "Maintenance frequency (shots) must be greater than 0.")]
    [InlineData(100_000, 0, "PM warning shots must be greater than 0.")]
    [InlineData(100_000, 100_000, "PM warning shots must be less than the maintenance frequency (shots).")]
    [InlineData(null, 5_000, "PM warning shots need a maintenance frequency (shots).")]
    public async Task MoldMaster_ValidatesThePmConfiguration(int? interval, int? warning, string message)
    {
        var w = Create();
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            w.MoldMaster.UpdateAsync(MoldId, UpdateRequest(w.Mold, interval: interval, warning: warning, keepNulls: true), 1, null, CancellationToken.None));

        Assert.Contains(message, ex.Errors);
    }

    // ------------------------------------------------------------------ failures and rollback

    [Fact]
    public async Task NotificationFailure_NeverFailsTheProductionEntry_AndTheWarningIsRetriedNextTime()
    {
        var w = Create();
        w.Notifications.FailNextAdd = true;

        await w.ProduceToAsync(591_000); // succeeds
        Assert.Equal(591_000, w.Mold.CurrentUsageShots);
        Assert.Equal(0, w.Warnings);
        Assert.DoesNotContain(w.Audit.Entries, e => e.Action == MoldPmAuditNames.WarningRaised);

        await w.ProduceToAsync(592_000);
        Assert.Equal(1, w.Warnings);
    }

    [Fact]
    public async Task DueNotificationFailure_StillCreatesThePm()
    {
        var w = Create();
        w.Notifications.FailNextAdd = true;
        await w.ProduceToAsync(600_000);

        Assert.Single(w.Pms.Pms);
        Assert.Equal(0, w.DueNotices);
        Assert.Contains(w.Audit.Entries, e => e.Action == MoldPmAuditNames.AutoCreated);
    }

    private sealed class ThrowingPmRepository : IMoldPmRepository
    {
        private readonly InMemoryMoldPmRepository _inner;
        public ThrowingPmRepository(InMemoryMoldPmRepository inner) => _inner = inner;
        public Task<(IReadOnlyList<MoldPm> Items, int TotalCount)> GetAllAsync(MoldPmListQuery r, DateOnly t, CancellationToken c) => _inner.GetAllAsync(r, t, c);
        public Task<MoldPmCountsDto> GetCountsAsync(MoldPmListQuery r, DateOnly t, CancellationToken c) => _inner.GetCountsAsync(r, t, c);
        public Task<MoldPm?> GetByIdAsync(int id, CancellationToken c) => _inner.GetByIdAsync(id, c);
        public Task<IReadOnlyList<MoldUsageSnapshot>> GetMoldUsageAsync(CancellationToken c) => _inner.GetMoldUsageAsync(c);
        public Task<bool> HasOpenAutomaticPmAsync(int moldId, CancellationToken c) => _inner.HasOpenAutomaticPmAsync(moldId, c);
        public Task<MoldPm> AddAutomaticPmAsync(MoldPm pm, CancellationToken c) => throw new InvalidOperationException("No active sequence is configured for MOLD_PM.");
        public Task<MoldPm> StartAsync(MoldPm pm, byte[] rv, Action<Mold> a, CancellationToken c) => _inner.StartAsync(pm, rv, a, c);
        public Task<MoldPm> CompleteAsync(MoldPm pm, byte[] rv, Action<Mold> a, Func<Mold, CancellationToken, Task> s, CancellationToken c) => _inner.CompleteAsync(pm, rv, a, s, c);
    }

    [Fact]
    public async Task PmCreationFailure_RollsBackTheWholeProductionEntry()
    {
        var w = Create(wrapPms: inner => new ThrowingPmRepository(inner));
        await w.ProduceToAsync(599_000);

        await Assert.ThrowsAsync<InvalidOperationException>(() => w.ProduceToAsync(600_000));

        Assert.Equal(599_000, w.Mold.CurrentUsageShots); // the usage increment was not committed
        Assert.Equal(2, w.Entries.AddCalls);             // two attempts ...
        Assert.Single((await w.Production.GetAllAsync(new ProductionEntryListQuery(), CancellationToken.None)).Items); // ... one entry
        Assert.Empty(w.Pms.Pms);
    }

    // ------------------------------------------------------------------ workflow: start / complete / tabs / audit

    [Fact]
    public async Task Start_ThenComplete_MoldGoesToMaintenanceAndBack()
    {
        var w = Create();
        await w.ProduceToAsync(600_000);

        var started = await w.MoldPm.StartAsync(w.OpenPm.MoldPmId, new StartMoldPmRequest { RowVersion = Convert.ToBase64String(w.OpenPm.RowVersion) }, 2, null, CancellationToken.None);
        Assert.Equal((MoldPmStatus.InProgress, MoldStatus.Maintenance, MoldPmState.InMaintenance), (started.Status, w.Mold.Status, (await w.UsageAsync()).State));

        await Assert.ThrowsAsync<ConflictException>(() =>
            w.MoldPm.StartAsync(w.OpenPm.MoldPmId, new StartMoldPmRequest { RowVersion = Convert.ToBase64String(w.OpenPm.RowVersion) }, 2, null, CancellationToken.None));

        await w.CompleteOpenAsync();
        Assert.Equal(MoldStatus.InProduction, w.Mold.Status); // BR-25
        Assert.Contains(w.Audit.Entries, e => e.Action == MoldPmAuditNames.Started && e.UserId == 2);
    }

    [Fact]
    public async Task Complete_WithoutStarting_KeepsTheMoldStatus()
    {
        var w = Create(m => m.Status = MoldStatus.Available);
        await w.ProduceToAsync(600_000);
        await w.CompleteOpenAsync();

        Assert.Equal(MoldStatus.Available, w.Mold.Status);
    }

    [Fact]
    public async Task Complete_Validation_Stale_AndAlreadyCompleted()
    {
        var w = Create();
        await w.ProduceToAsync(600_000);
        var id = w.OpenPm.MoldPmId;
        var rv = Convert.ToBase64String(w.OpenPm.RowVersion);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => w.MoldPm.CompleteAsync(id, new CompleteMoldPmRequest { MaintenanceBy = "  ", RowVersion = rv }, 2, null, CancellationToken.None));
        Assert.Contains("MaintenanceBy is required.", ex.Errors);
        ex = await Assert.ThrowsAsync<ValidationException>(() => w.MoldPm.CompleteAsync(id, new CompleteMoldPmRequest { MaintenanceBy = new string('x', 101), Remarks = new string('r', 1001) }, 2, null, CancellationToken.None));
        Assert.Equal(new[] { "MaintenanceBy must be at most 100 characters.", "Remarks must be at most 1000 characters.", "RowVersion is required." }, ex.Errors);

        await Assert.ThrowsAsync<ConflictException>(() => w.MoldPm.CompleteAsync(id, new CompleteMoldPmRequest { MaintenanceBy = "Ravi", RowVersion = Convert.ToBase64String(new byte[] { 99 }) }, 2, null, CancellationToken.None));
        Assert.Equal(MoldPmStatus.Scheduled, w.OpenPm.Status); // nothing written
        Assert.Equal(500_000, w.Mold.PmCycleStartShots);

        await w.MoldPm.CompleteAsync(id, new CompleteMoldPmRequest { MaintenanceBy = "Ravi", RowVersion = rv }, 2, null, CancellationToken.None);
        var again = await Assert.ThrowsAsync<ConflictException>(() => w.MoldPm.CompleteAsync(id, new CompleteMoldPmRequest { MaintenanceBy = "Ravi", RowVersion = rv }, 2, null, CancellationToken.None));
        Assert.Equal("The maintenance record is already completed.", again.Message);
        Assert.Equal(600_000, w.Mold.PmCycleStartShots); // the cycle moved exactly once
    }

    [Fact]
    public async Task Tabs_DueToday_OverdueTomorrow_InProgress_Completed()
    {
        var w = Create();
        await w.ProduceToAsync(600_000);
        var q = new MoldPmListQuery();

        Assert.Equal((1, 0, 0, 0), Counts(await w.MoldPm.GetCountsAsync(q, CancellationToken.None)));
        Assert.False((await w.MoldPm.GetByIdAsync(w.OpenPm.MoldPmId, CancellationToken.None)).IsOverdue);

        w.Clock.UtcNow = w.Clock.UtcNow.AddDays(1);
        Assert.Equal((0, 1, 0, 0), Counts(await w.MoldPm.GetCountsAsync(q, CancellationToken.None)));
        var dto = (await w.MoldPm.GetAllAsync(new MoldPmListQuery { Bucket = MoldPmBucket.Overdue }, CancellationToken.None)).Items.Single();
        Assert.True(dto.IsOverdue);
        Assert.Equal((600_000L, (long?)0L, true), ((long)dto.ThresholdShots!, dto.RemainingShots, dto.CreatedBySystem));

        await w.MoldPm.StartAsync(w.OpenPm.MoldPmId, new StartMoldPmRequest { RowVersion = Convert.ToBase64String(w.OpenPm.RowVersion) }, 2, null, CancellationToken.None);
        Assert.Equal((0, 0, 1, 0), Counts(await w.MoldPm.GetCountsAsync(q, CancellationToken.None)));

        await w.CompleteOpenAsync();
        Assert.Equal((0, 0, 0, 1), Counts(await w.MoldPm.GetCountsAsync(q, CancellationToken.None)));

        await Assert.ThrowsAsync<ValidationException>(() => w.MoldPm.GetAllAsync(new MoldPmListQuery { Bucket = "pending" }, CancellationToken.None));
    }

    [Fact]
    public async Task AutomaticActions_AreAuditedAsTheSystem_UserActionsAsTheUser()
    {
        var w = Create();
        await w.ProduceToAsync(590_000);
        await w.ProduceToAsync(600_000);
        await w.CompleteOpenAsync();

        var auto = w.Audit.Entries.Where(e => e.Action is MoldPmAuditNames.AutoCreated or MoldPmAuditNames.WarningRaised).ToList();
        Assert.Equal(2, auto.Count);
        Assert.All(auto, e => Assert.Equal(("System", (int?)null), (e.UserName, e.UserId)));
        Assert.Contains(auto, e => e.Description.Contains("Triggered by production entry PROD-") && e.Description.Contains("by Sakthi"));

        var completed = w.Audit.Entries.Single(e => e.Action == MoldPmAuditNames.Completed);
        Assert.Equal(((int?)2, MoldPmAuditNames.Module), (completed.UserId, completed.Module));
        Assert.All(w.Audit.Entries, e => Assert.True(e.Action.Length <= 30));
    }

    private static (int, int, int, int) Counts(MoldPmCountsDto c) => (c.Due, c.Overdue, c.InProgress, c.Completed);

    private static UpdateMoldRequest UpdateRequest(Mold m, string? status = null, int? usage = null, int? interval = null, int? warning = null, bool keepNulls = false) => new()
    {
        MoldName = m.MoldName, ProductId = m.ProductId, MoldType = m.MoldType, CavityCount = m.CavityCount,
        MaximumShots = m.MaximumShots, WarningShots = m.WarningShots, ReplacementShots = m.ReplacementShots,
        MaintenanceFrequencyShots = keepNulls ? interval : interval ?? m.MaintenanceFrequencyShots,
        PmWarningShots = keepNulls ? warning : warning ?? m.PmWarningShots,
        CurrentUsageShots = usage ?? m.CurrentUsageShots, Status = status ?? m.Status,
        RowVersion = Convert.ToBase64String(m.RowVersion),
    };
}
