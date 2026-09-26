using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The real ProductionEntryService against in-memory fakes (see ProductionEntryTestData). Acting user 1 "Sakthi".
/// </summary>
public class ProductionEntryServiceTests
{
    private static readonly DateOnly Day = new(2026, 9, 25);

    private sealed record Sut(ProductionEntryService Service, InMemoryProductionEntryRepository Entries, RecordingAuditLog Audit, InMemoryUserRepository Users);

    private static Sut Create(IAuditLogService? auditOverride = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machineList = MachineTestData.Machines();
        var productList = ProductTestData.Products();
        var moldList = ProductionEntryTestData.Molds();
        var machines = new InMemoryMachineRepository(machineList, departments, employees);
        var products = new InMemoryProductRepository(productList);
        var molds = new InMemoryMoldRepository(moldList, products, employees);
        var entries = new InMemoryProductionEntryRepository(moldList, machineList, productList);
        var audit = new RecordingAuditLog();
        var evaluator = MoldPmTestFactory.Evaluator(new InMemoryMoldPmRepository(moldList), new InMemoryNotificationRepository(), new FixedClock(), auditOverride ?? audit);
        var service = new ProductionEntryService(entries, machines, products, molds, evaluator, users, new FixedClock(), auditOverride ?? audit, NullLogger<ProductionEntryService>.Instance);
        return new Sut(service, entries, audit, users);
    }

    private static CreateProductionEntryRequest Req(int mold = 1, int? qty = 1_000, int? rejected = 0, int? machine = 1, int? product = 1, string? shift = "Shift A", string? remarks = null, DateOnly? date = null) =>
        new() { EntryDate = date ?? Day, Shift = shift, MachineId = machine, ProductId = product, MoldId = mold, ProductionQty = qty, RejectedQty = rejected, Remarks = remarks };

    // ================================================================ create

    [Fact]
    public async Task Create_IssuesTheNumber_StoresTheUsageSnapshot_UpdatesTheMold_AndAudits()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(Req(qty: 1_200, rejected: 50, shift: " shift b ", remarks: "  night run "), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("PROD-0001", dto.EntryNo);
        Assert.Equal("Shift B", dto.Shift); // normalized onto the CK value
        Assert.Equal(1_150, dto.GoodQty);
        Assert.Equal((100_000, 101_200), (dto.MoldUsageBefore, dto.MoldUsageAfter));
        Assert.Equal(ProductionEntryStatus.Saved, dto.Status);
        Assert.Equal("night run", dto.Remarks);
        Assert.Equal(("MAC-0001", "PRD-0001", "MLD-0001"), (dto.MachineCode, dto.ProductCode, dto.MoldCode));
        Assert.Equal(1, dto.CreatedBy);
        Assert.Equal(FixedClock.Now, dto.CreatedAt);

        var mold = s.Entries.StoredMold(1);
        Assert.Equal(101_200, mold.CurrentUsageShots);
        Assert.Equal(MoldStatus.Available, mold.Status); // below warning -> unchanged (8.3)
        Assert.Equal((FixedClock.Now, (int?)1), (mold.UpdatedAt!.Value, mold.UpdatedBy));

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("ProductionEntryCreated", entry.Action);
        Assert.True(entry.Action.Length <= 30); // audit_log.action VARCHAR(30)
        Assert.Equal("Production Entry", entry.Module);
        Assert.Equal("ProductionEntry", entry.EntityName);
        Assert.Equal(dto.ProductionEntryId, entry.EntityId);
        Assert.Equal("PROD-0001", entry.RecordRef);
        Assert.Equal(("Sakthi", "10.0.0.5", (int?)1), (entry.UserName, entry.IpAddress, entry.UserId));
        Assert.Contains("Mold usage updated from 100,000 to 101,200.", entry.Description);
        Assert.Contains(entry.Details, d => d is { FieldName: "mold_usage", OldValue: "100000", NewValue: "101200" });
        Assert.DoesNotContain(entry.Details, d => d.FieldName == "mold_status");
    }

    [Fact]
    public async Task Create_NumbersAreSequential()
    {
        var s = Create();

        var a = await s.Service.CreateAsync(Req(qty: 10), 1, null, CancellationToken.None);
        var b = await s.Service.CreateAsync(Req(qty: 10), 1, null, CancellationToken.None);

        Assert.Equal(("PROD-0001", "PROD-0002"), (a.EntryNo, b.EntryNo));
    }

    [Fact]
    public async Task Create_RejectedDefaultsToZero()
    {
        var dto = await Create().Service.CreateAsync(Req(qty: 40, rejected: null), 1, null, CancellationToken.None);

        Assert.Equal((0, 40), (dto.RejectedQty, dto.GoodQty));
    }

    [Theory]
    [InlineData("Shift A")]
    [InlineData("Shift B")]
    [InlineData("Shift C")]
    public async Task Create_AcceptsEveryShiftTheCheckConstraintAllows(string shift)
    {
        var dto = await Create().Service.CreateAsync(Req(shift: shift), 1, null, CancellationToken.None);

        Assert.Equal(shift, dto.Shift);
    }

    [Fact]
    public async Task Create_RequiresEveryMandatoryField_ReportingThemTogether_WritingNothing()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(new CreateProductionEntryRequest(), 1, null, CancellationToken.None));

        Assert.Equal(
            new[] { "EntryDate is required.", "Shift is required.", "MachineId is required.", "ProductId is required.", "MoldId is required.", "ProductionQty is required." },
            ex.Errors);
        AssertNothingWritten(s);
    }

    [Theory]
    [InlineData(0, 0, "Production quantity must be greater than 0.")]
    [InlineData(-5, 0, "Production quantity must be greater than 0.")]
    [InlineData(10, -1, "Rejected quantity cannot be negative.")]
    [InlineData(10, 11, "Rejected quantity cannot be more than production quantity.")]
    public async Task Create_EnforcesTheQuantityRules(int qty, int rejected, string message)
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(Req(qty: qty, rejected: rejected), 1, null, CancellationToken.None));

        Assert.Contains(message, ex.Errors);
        AssertNothingWritten(s);
    }

    [Fact]
    public async Task Create_RejectedEqualToProduction_IsAllowed_GoodQtyZero()
    {
        var dto = await Create().Service.CreateAsync(Req(qty: 10, rejected: 10), 1, null, CancellationToken.None);

        Assert.Equal(0, dto.GoodQty);
    }

    [Fact]
    public async Task Create_RejectsAnUnknownShift_AndOverlongRemarks()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(Req(shift: "Night", remarks: new string('r', 501)), 1, null, CancellationToken.None));

        Assert.Contains("Shift must be one of: Shift A, Shift B, Shift C.", ex.Errors);
        Assert.Contains("Remarks must be at most 500 characters.", ex.Errors);
        AssertNothingWritten(s);

        var atLimit = await s.Service.CreateAsync(Req(remarks: new string('r', 500)), 1, null, CancellationToken.None);
        Assert.Equal(500, atLimit.Remarks!.Length);
    }

    [Fact]
    public async Task Create_UnknownMachineProductOrMold_IsNotFound_WritingNothing()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(Req(machine: 999), 1, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(Req(product: 999), 1, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(Req(mold: 999), 1, null, CancellationToken.None));

        AssertNothingWritten(s);
    }

    [Fact]
    public async Task Create_InactiveMachineOrProduct_IsRefused_BR13()
    {
        var s = Create();

        var machine = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(Req(machine: 3), 1, null, CancellationToken.None));
        var product = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(Req(mold: 5, product: 3), 1, null, CancellationToken.None));

        Assert.Contains("The selected machine is not active.", machine.Errors);
        Assert.Contains("The selected product is not active.", product.Errors);
        AssertNothingWritten(s);
    }

    [Fact]
    public async Task Create_MoldOfAnotherProduct_IsRefused()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(Req(mold: 3, product: 1), 1, null, CancellationToken.None));

        Assert.Contains("Selected mold does not belong to the product.", ex.Errors);
        AssertNothingWritten(s);
    }

    // ================================================================ mold life engine (BR-10 .. BR-12, 8.3)

    [Fact]
    public async Task Create_MoldAtItsReplacementLimit_IsAConflict_AndNothingChanges_BR10()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateAsync(Req(mold: 3, product: 2, qty: 1), 1, null, CancellationToken.None));

        Assert.Equal(ProductionEntryService.ReplacementLimitMessage, ex.Message);
        Assert.Equal(500_000, s.Entries.StoredMold(3).CurrentUsageShots);
        AssertNothingWritten(s);
    }

    [Fact]
    public async Task Create_CrossingTheWarningLevel_SetsInProduction_AndAuditsTheStatusChange()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(Req(mold: 2, qty: 20_000), 1, null, CancellationToken.None);

        Assert.Equal((440_000, 460_000), (dto.MoldUsageBefore, dto.MoldUsageAfter));
        Assert.Equal(MoldStatus.InProduction, s.Entries.StoredMold(2).Status);
        var entry = Assert.Single(s.Audit.Entries);
        Assert.Contains(entry.Details, d => d is { FieldName: "mold_status", OldValue: "Available", NewValue: "In Production" });
        Assert.Contains("Mold status changed from Available to In Production.", entry.Description);
    }

    [Fact]
    public async Task Create_MayOvershootTheLimit_Q13_ThenTheMoldIsReplacementDue_AndTheNextSaveIsBlocked()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(Req(mold: 2, qty: 100_000), 1, null, CancellationToken.None);

        Assert.Equal(540_000, dto.MoldUsageAfter); // only the usage BEFORE the entry is checked
        Assert.Equal(MoldStatus.ReplacementDue, s.Entries.StoredMold(2).Status);
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateAsync(Req(mold: 2, qty: 1), 1, null, CancellationToken.None));
        Assert.Equal(1, s.Entries.Count);
    }

    [Fact]
    public async Task Create_ARetiredMoldInTheWarningBand_StaysRetired()
    {
        var s = Create();

        await s.Service.CreateAsync(Req(mold: 4, qty: 20), 1, null, CancellationToken.None);

        Assert.Equal((450_010, MoldStatus.Retired), (s.Entries.StoredMold(4).CurrentUsageShots, s.Entries.StoredMold(4).Status));
    }

    [Theory]
    [InlineData(MoldStatus.Available, 100, MoldStatus.Available)]
    [InlineData(MoldStatus.Maintenance, 449_999, MoldStatus.Maintenance)]
    [InlineData(MoldStatus.Available, 450_000, MoldStatus.InProduction)]
    [InlineData(MoldStatus.Maintenance, 499_999, MoldStatus.InProduction)]
    [InlineData(MoldStatus.Retired, 460_000, MoldStatus.Retired)]
    [InlineData(MoldStatus.Retired, 500_000, MoldStatus.ReplacementDue)]
    [InlineData(MoldStatus.InProduction, 600_000, MoldStatus.ReplacementDue)]
    public void MoldLifeEngine_StatusAfterProduction_FollowsSection83(string current, int usageAfter, string expected)
    {
        Assert.Equal(expected, MoldLifeEngine.StatusAfterProduction(current, usageAfter, 450_000, 500_000));
    }

    [Theory]
    [InlineData(499_999, true)]
    [InlineData(500_000, false)]
    [InlineData(510_000, false)]
    public void MoldLifeEngine_ProductionAllowedOnlyBelowReplacementShots(int usage, bool allowed)
    {
        Assert.Equal(allowed, MoldLifeEngine.IsProductionAllowed(usage, 500_000));
    }

    [Fact]
    public async Task Create_AUsageThatWouldOverflowTheIntColumn_IsRefused()
    {
        var s = Create();
        s.Entries.StoredMold(1).CurrentUsageShots = 10;
        s.Entries.StoredMold(1).ReplacementShots = int.MaxValue;

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(Req(qty: int.MaxValue), 1, null, CancellationToken.None));

        Assert.Contains("beyond the largest value", Assert.Single(ex.Errors));
        AssertNothingWritten(s, moldId: 1, usage: 10);
    }

    [Fact]
    public async Task Create_WhenTheSaveFails_NothingIsAudited_AndTheMoldAndSequenceAreUntouched()
    {
        var s = Create();
        s.Entries.FailNextSave = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => s.Service.CreateAsync(Req(), 1, null, CancellationToken.None));

        AssertNothingWritten(s);
        var next = await s.Service.CreateAsync(Req(qty: 5), 1, null, CancellationToken.None);
        Assert.Equal("PROD-0001", next.EntryNo); // the failed save consumed no number
    }

    // ================================================================ list / get / lookups

    [Fact]
    public async Task GetAll_IsNewestFirst_PagedTenAtATime_AndFilters()
    {
        var s = Create();
        for (var i = 1; i <= 23; i++)
        {
            await s.Service.CreateAsync(Req(qty: 1, machine: i % 2 == 0 ? 2 : 1, date: Day.AddDays(i)), 1, null, CancellationToken.None);
        }

        var numbers = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await s.Service.GetAllAsync(new ProductionEntryListQuery { PageNumber = page, PageSize = 10 }, CancellationToken.None);
            Assert.Equal(23, result.TotalCount);
            Assert.Equal(3, result.TotalPages);
            Assert.Equal(page < 3 ? 10 : 3, result.Items.Count);
            numbers.AddRange(result.Items.Select(i => i.EntryNo));
        }

        Assert.Equal(Enumerable.Range(1, 23).Reverse().Select(i => $"PROD-{i:0000}"), numbers); // 23..14, 13..4, 3..1

        async Task<int> Count(ProductionEntryListQuery q) => (await s.Service.GetAllAsync(q, CancellationToken.None)).TotalCount;
        Assert.Equal(11, await Count(new ProductionEntryListQuery { MachineId = 2 }));
        Assert.Equal(23, await Count(new ProductionEntryListQuery { MoldId = 1 }));
        Assert.Equal(3, await Count(new ProductionEntryListQuery { FromDate = Day.AddDays(21) }));
        Assert.Equal(5, await Count(new ProductionEntryListQuery { FromDate = Day.AddDays(2), ToDate = Day.AddDays(6) }));
        Assert.Equal(1, await Count(new ProductionEntryListQuery { Search = "prod-0007" }));
    }

    [Fact]
    public void ListQuery_DefaultsToTenPerPage()
    {
        Assert.Equal((1, 10), (new ProductionEntryListQuery().PageNumber, new ProductionEntryListQuery().PageSize));
    }

    [Fact]
    public async Task GetById_ReturnsTheEntryWithNames_AndThrowsNotFoundForUnknown()
    {
        var s = Create();
        var created = await s.Service.CreateAsync(Req(), 1, null, CancellationToken.None);

        var dto = await s.Service.GetByIdAsync(created.ProductionEntryId, CancellationToken.None);

        Assert.Equal((created.EntryNo, "Rubber Seal A", "MLD-0001"), (dto.EntryNo, dto.ProductName, dto.MoldCode));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.GetByIdAsync(999, CancellationToken.None));
    }

    [Fact]
    public async Task Lookups_ListActiveMachinesAndProducts_AndMoldsOfActiveProductsInAnyStatus()
    {
        var lookups = await Create().Service.GetLookupsAsync(CancellationToken.None);

        Assert.Equal(new[] { 1, 2 }, lookups.Machines.Select(m => m.MachineId));
        Assert.Equal(new[] { 1, 2 }, lookups.Products.Select(p => p.ProductId));
        Assert.Equal(new[] { 1, 2, 3, 4 }, lookups.Molds.Select(m => m.MoldId)); // 5 belongs to the inactive product
        Assert.Contains(lookups.Molds, m => m is { MoldId: 3, LifeState: "Replace", Status: "Replacement Due" });
    }

    // ================================================================ audit failure behaviour

    [Fact]
    public async Task WhenTheActingUsersNameCannotBeResolved_TheSaveStillSucceeds_AuditedAsUnknown()
    {
        var s = Create();
        s.Users.ThrowOnGetByIdCall = 1;

        var dto = await s.Service.CreateAsync(Req(), 1, null, CancellationToken.None);

        Assert.Equal("PROD-0001", dto.EntryNo);
        Assert.Equal("Unknown", Assert.Single(s.Audit.Entries).UserName);
    }

    [Fact]
    public async Task WhenWritingTheAuditRowFails_TheSaveStillSucceeds()
    {
        var audit = new AuditLogService(new ThrowingAuditRepository(), new FixedClock(), NullLogger<AuditLogService>.Instance);
        var s = Create(auditOverride: audit);

        var dto = await s.Service.CreateAsync(Req(), 1, null, CancellationToken.None);

        Assert.Equal("PROD-0001", dto.EntryNo);
        Assert.Equal(101_000, s.Entries.StoredMold(1).CurrentUsageShots);
    }

    private static void AssertNothingWritten(Sut s, int moldId = 1, int? usage = null)
    {
        Assert.Equal(0, s.Entries.Count);
        Assert.Equal(0, s.Entries.LastNumber);
        Assert.Empty(s.Audit.Entries);
        Assert.Equal(usage ?? ProductionEntryTestData.Molds().Single(m => m.MoldId == moldId).CurrentUsageShots, s.Entries.StoredMold(moldId).CurrentUsageShots);
    }

    private sealed class ThrowingAuditRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit persistence failure.");
    }
}
