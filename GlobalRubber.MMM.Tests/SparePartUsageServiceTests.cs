using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Domain.Rules;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>The real SparePartUsageService over in-memory fakes. Acting user 1 "Sakthi"; today = the MovableClock's date.</summary>
public class SparePartUsageServiceTests
{
    private sealed record Sut(
        SparePartUsageService Service, InMemorySparePartUsageRepository Usages, List<SparePart> Parts, InMemoryNotificationRepository Notifications,
        RecordingAuditLog Audit, MovableClock Clock)
    {
        public SparePart Part(int id) => Parts.Single(p => p.SparePartId == id);

        public Task<(SparePartUsageDto Usage, bool Created)> Post(int partId, decimal qty, string type = SparePartUsageMaintenanceType.MachinePm, int pmId = 20,
            Guid? requestId = null, int? employeeId = 1) =>
            Service.CreateAsync(new CreateSparePartUsageRequest
            {
                MaintenanceType = type, MaintenanceId = pmId, SparePartId = partId, Quantity = qty, UsageDate = Clock.Today,
                UsedByEmployeeId = employeeId, Remarks = "during PM", RequestId = requestId,
            }, 1, "10.0.0.1", CancellationToken.None);
    }

    private static Sut Create()
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machines = new InMemoryMachineRepository(MachineTestData.Machines(), departments, employees);
        var vendors = new InMemoryVendorRepository(VendorTestData.Vendors());
        var partList = SparePartUsageTestData.Parts();
        var parts = new InMemorySparePartRepository(partList, machines, vendors);
        var clock = new MovableClock();
        var usages = new InMemorySparePartUsageRepository(partList, clock.Today);
        var notifications = new InMemoryNotificationRepository();
        var audit = new RecordingAuditLog();
        var service = new SparePartUsageService(usages, parts, employees, notifications, users, clock, audit, NullLogger<SparePartUsageService>.Instance);
        return new Sut(service, usages, partList, notifications, audit, clock);
    }

    // ------------------------------------------------------------------ posting

    [Fact]
    public async Task MachinePmUsage_DeductsStock_LinksTheMaintenance_SnapshotsCost_WritesTheLedger_AndAudits()
    {
        var s = Create();
        var (dto, created) = await s.Post(10, 3);

        Assert.True(created);
        Assert.Equal(7, s.Part(10).CurrentStock);
        Assert.Equal(("SPU-0001", 3, "Nos", "Machine PM", "MPM-0010", (int?)20, (int?)null, (int?)1, (int?)null), (dto.UsageNo, dto.Quantity, dto.Unit, dto.MaintenanceType, dto.MaintenanceNo, dto.MachinePmId, dto.MoldPmId, dto.MachineId, dto.MoldId));
        Assert.Equal(((decimal?)250.00m, (decimal?)750.00m, "Posted"), (dto.UnitCostAtIssue, dto.UsageCost, dto.Status));

        var ledger = Assert.Single(s.Usages.Ledger);
        Assert.Equal((SparePartStockTransactionType.Issue, -3, 10, 7, SparePartStockReferenceType.SparePartUsage, (int?)1, "SPU-0001 (MPM-0010)"),
            (ledger.TransactionType, ledger.Quantity, ledger.PreviousStock, ledger.NewStock, ledger.ReferenceType, ledger.ReferenceId, ledger.ReferenceNo));
        var movement = Assert.Single(dto.StockMovements);
        Assert.Equal((10, 7), (movement.PreviousStock, movement.NewStock));

        var audit = Assert.Single(s.Audit.Entries);
        Assert.Equal(("SparePartUsageCreated", "Spare Part Usage", "SPU-0001", (int?)1), (audit.Action, audit.Module, audit.RecordRef, audit.UserId));
        Assert.Equal("Sakthi issued 3 Nos of Bearing 6204 (SPR-0010) for MPM-0010 (MAC-0001) - SPU-0001. Stock changed from 10 to 7.", audit.Description);
        Assert.Contains(audit.Details, d => d.FieldName == "current_stock (SPR-0010)" && d.OldValue == "10" && d.NewValue == "7");
        Assert.Contains(audit.Details, d => d.FieldName == "maintenance_no" && d.NewValue == "MPM-0010");
    }

    [Fact]
    public async Task MoldPmUsage_LinksTheMoldAndItsPm()
    {
        var s = Create();
        var (dto, _) = await s.Post(11, 1, SparePartUsageMaintenanceType.MoldPm, 30);

        Assert.Equal(("Mold PM", "MPMD-0005", (int?)30, (int?)null, (int?)1, (int?)null, "Mold Maintenance"),
            (dto.MaintenanceType, dto.MaintenanceNo, dto.MoldPmId, dto.MachinePmId, dto.MoldId, dto.MachineId, s.Usages.Usages.Single().UsedFor));
        Assert.Equal(4, s.Part(11).CurrentStock);
    }

    [Fact]
    public async Task OnePm_ManySpareParts_EachItsOwnUsageLine()
    {
        var s = Create();
        await s.Post(10, 2);
        await s.Post(11, 1);
        await s.Post(10, 1);

        Assert.Equal(3, s.Usages.Usages.Count);
        Assert.All(s.Usages.Usages, u => Assert.Equal(20, u.MachinePmId));
        Assert.Equal((7, 4), (s.Part(10).CurrentStock, s.Part(11).CurrentStock));
        Assert.Equal(3, s.Usages.Ledger.Count);
    }

    [Theory]
    [InlineData(0, "Quantity must be greater than 0.")]
    [InlineData(-2, "Quantity must be greater than 0.")]
    [InlineData(1.5, "Quantity must be a whole number.")]
    [InlineData(0.75, "Quantity must be a whole number.")]
    public async Task InvalidQuantity_Refused_NothingWritten(double qty, string message)
    {
        var s = Create();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Post(10, (decimal)qty));

        Assert.Contains(message, ex.Errors);
        Assert.Equal(10, s.Part(10).CurrentStock);
        Assert.Empty(s.Usages.Usages);
    }

    [Fact]
    public async Task MissingFields_AllReportedTogether()
    {
        var s = Create();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(new CreateSparePartUsageRequest(), 1, null, CancellationToken.None));

        Assert.Equal(new[] { "MaintenanceType is required.", "MaintenanceId is required.", "SparePartId is required.", "Quantity is required.", "UsageDate is required." }, ex.Errors);
    }

    [Fact]
    public async Task InsufficientStock_409_NothingWritten_StockNeverNegative()
    {
        var s = Create();
        await s.Post(11, 3); // Grease 5 -> 2

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Post(11, 5));

        Assert.Equal("Insufficient stock available. Available: 2 Kg, requested: 5 Kg.", ex.Message);
        Assert.Equal(2, s.Part(11).CurrentStock);
        Assert.Single(s.Usages.Usages);
        Assert.Single(s.Usages.Ledger);
        Assert.Equal(1, s.Usages.SequenceNumber); // no number consumed
    }

    [Fact]
    public async Task IssuingEverything_ReachesZero_OutOfStock_OneNotification_AndSystemAudit()
    {
        var s = Create();
        await s.Post(11, 5);

        Assert.Equal(0, s.Part(11).CurrentStock);
        var n = Assert.Single(s.Notifications.Items);
        Assert.Equal((NotificationTypes.SparePartOutOfStock, ModuleCodes.MasterSparePart, NotificationSeverity.Critical, (int?)null),
            (n.NotificationType, n.ModuleCode, n.Severity, n.CreatedBy));
        Assert.Contains("SPR-0011 - Grease EP2 is out of stock (0 Kg) after SPU-0001 for MPM-0010", n.Message);
        var system = Assert.Single(s.Audit.Entries, e => e.Action == "SparePartOutOfStock");
        Assert.Equal(("System", (int?)null), (system.UserName, system.UserId));
        await Assert.ThrowsAsync<ConflictException>(() => s.Post(11, 1));
    }

    [Fact]
    public async Task LowStock_NotifiesOnlyOnTheTransition_10_8_6_4_3()
    {
        var s = Create(); // Bearing 10, minimum 5
        await s.Post(10, 2); // 8
        await s.Post(10, 2); // 6
        Assert.Empty(s.Notifications.Items);

        await s.Post(10, 2); // 4 -> Low Stock
        var low = Assert.Single(s.Notifications.Items);
        Assert.Equal(NotificationTypes.SparePartLowStock, low.NotificationType);
        Assert.Contains("low on stock: 4 Nos left (minimum 5)", low.Message);

        await s.Post(10, 1); // 3 - still Low: no second notification
        Assert.Single(s.Notifications.Items);
        Assert.Single(s.Audit.Entries, e => e.Action == "SparePartLowStock");
        Assert.Equal(SparePartStockStatus.LowStock, s.Part(10).StockStatus);
    }

    [Fact]
    public async Task NotificationFailure_NeverFailsTheUsage()
    {
        var s = Create();
        s.Notifications.FailNextAdd = true;
        await s.Post(10, 6); // 10 -> 4, Low Stock, notification write fails

        Assert.Equal(4, s.Part(10).CurrentStock);
        Assert.Empty(s.Notifications.Items);
        Assert.Contains(s.Audit.Entries, e => e.Action == "SparePartLowStock" && e.Description.EndsWith("The notification could not be written."));
    }

    // ------------------------------------------------------------------ references

    [Fact]
    public async Task InactiveSparePart_Refused()
    {
        var s = Create();
        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Post(12, 1));
        Assert.Contains("The selected spare part is not active.", ex.Errors);
        Assert.Equal(8, s.Part(12).CurrentStock);
    }

    [Fact]
    public async Task UnknownSparePart_Or_UnknownMaintenance_404()
    {
        var s = Create();
        await Assert.ThrowsAsync<NotFoundException>(() => s.Post(999, 1));
        var ex = await Assert.ThrowsAsync<NotFoundException>(() => s.Post(10, 1, pmId: 999));
        Assert.Equal("The selected Machine PM (999) was not found.", ex.Message);
        await Assert.ThrowsAsync<NotFoundException>(() => s.Post(10, 1, SparePartUsageMaintenanceType.MoldPm, 20)); // a Machine PM id is not a Mold PM
        Assert.Empty(s.Usages.Usages);
    }

    [Fact]
    public async Task CompletedPm_OrFutureMachinePm_Refused()
    {
        var s = Create();
        var completed = await Assert.ThrowsAsync<ValidationException>(() => s.Post(10, 1, pmId: 21));
        Assert.Contains("Spare parts can only be issued against open maintenance: MPM-0011 is already completed.", completed.Errors);
        var completedMold = await Assert.ThrowsAsync<ValidationException>(() => s.Post(10, 1, SparePartUsageMaintenanceType.MoldPm, 31));
        Assert.Contains("MPMD-0006 is already completed", completedMold.Errors.Single());
        var future = await Assert.ThrowsAsync<ValidationException>(() => s.Post(10, 1, pmId: 22));
        Assert.StartsWith("MPM-0012 is not due yet", future.Errors.Single());
        Assert.Equal(10, s.Part(10).CurrentStock);
    }

    [Fact]
    public async Task UsedBy_MustBeAnActiveEmployee_Optional()
    {
        var s = Create();
        await Assert.ThrowsAsync<NotFoundException>(() => s.Post(10, 1, employeeId: 99));
        var inactive = await Assert.ThrowsAsync<ValidationException>(() => s.Post(10, 1, employeeId: 3));
        Assert.Contains("The selected employee (Used By) is not active.", inactive.Errors);
        var (dto, _) = await s.Post(10, 1, employeeId: null);
        Assert.Null(dto.UsedByEmployeeId);
    }

    // ------------------------------------------------------------------ duplicate submission

    [Fact]
    public async Task SameRequestIdTwice_PostsOnce_ReturnsTheSameUsage()
    {
        var s = Create();
        var id = Guid.NewGuid();
        var (first, created1) = await s.Post(10, 3, requestId: id);
        var (second, created2) = await s.Post(10, 3, requestId: id);

        Assert.True(created1);
        Assert.False(created2);
        Assert.Equal(first.UsageNo, second.UsageNo);
        Assert.Single(s.Usages.Usages);
        Assert.Equal(7, s.Part(10).CurrentStock); // deducted once
    }

    [Fact]
    public async Task SameRequestId_DifferentData_409()
    {
        var s = Create();
        var id = Guid.NewGuid();
        await s.Post(10, 3, requestId: id);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Post(10, 4, requestId: id));
        Assert.Equal("This request id was already used for a different spare part usage.", ex.Message);
        Assert.Equal(7, s.Part(10).CurrentStock);
    }

    [Fact]
    public async Task DifferentRequestIds_SamePartAndPm_AreTwoLegitimateUsages()
    {
        var s = Create();
        await s.Post(10, 1, requestId: Guid.NewGuid());
        await s.Post(10, 1, requestId: Guid.NewGuid());
        Assert.Equal((2, 8), (s.Usages.Usages.Count, s.Part(10).CurrentStock));
    }

    // ------------------------------------------------------------------ rollback

    [Fact]
    public async Task FailureAfterUsageInsert_OrAfterStockUpdate_RollsBackEverything()
    {
        var s = Create();
        s.Usages.FailAfterUsageInsert = true;
        await Assert.ThrowsAnyAsync<Exception>(() => s.Post(10, 3));
        s.Usages.FailAfterUsageInsert = false;
        s.Usages.FailAfterStockUpdate = true;
        await Assert.ThrowsAnyAsync<Exception>(() => s.Post(10, 3));

        Assert.Equal(10, s.Part(10).CurrentStock);
        Assert.Empty(s.Usages.Usages);
        Assert.Empty(s.Usages.Ledger);
        Assert.Empty(s.Audit.Entries);
        Assert.Equal(0, s.Usages.SequenceNumber);
    }

    // ------------------------------------------------------------------ reversal

    [Fact]
    public async Task Reverse_RestoresStock_KeepsTheRecord_WritesAReversalRow_AndAudits()
    {
        var s = Create();
        var (dto, _) = await s.Post(10, 3);

        var reversed = await s.Service.ReverseAsync(dto.SparePartUsageId, new ReverseSparePartUsageRequest { Reason = "Wrong part picked", RowVersion = dto.RowVersion }, 1, null, CancellationToken.None);

        Assert.Equal((SparePartUsageStatus.Reversed, "Wrong part picked", 10), (reversed.Status, reversed.ReversalReason, s.Part(10).CurrentStock));
        Assert.Single(s.Usages.Usages); // never deleted
        Assert.Equal(2, reversed.StockMovements.Count);
        var reversal = s.Usages.Ledger.Last();
        Assert.Equal((SparePartStockTransactionType.Reversal, 3, 7, 10), (reversal.TransactionType, reversal.Quantity, reversal.PreviousStock, reversal.NewStock));
        Assert.Contains(s.Audit.Entries, e => e.Action == "SparePartUsageReversed" && e.Description.Contains("Stock restored from 7 to 10."));
    }

    [Fact]
    public async Task Reverse_Validation_Stale_AndTwice()
    {
        var s = Create();
        var (dto, _) = await s.Post(10, 3);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.ReverseAsync(dto.SparePartUsageId, new ReverseSparePartUsageRequest { Reason = " " }, 1, null, CancellationToken.None));
        Assert.Equal(new[] { "Reason is required.", "RowVersion is required." }, ex.Errors);
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.ReverseAsync(dto.SparePartUsageId, new ReverseSparePartUsageRequest { Reason = "x", RowVersion = Convert.ToBase64String(new byte[] { 99 }) }, 1, null, CancellationToken.None));
        Assert.Equal(7, s.Part(10).CurrentStock);

        await s.Service.ReverseAsync(dto.SparePartUsageId, new ReverseSparePartUsageRequest { Reason = "x", RowVersion = dto.RowVersion }, 1, null, CancellationToken.None);
        var again = await Assert.ThrowsAsync<ConflictException>(() => s.Service.ReverseAsync(dto.SparePartUsageId, new ReverseSparePartUsageRequest { Reason = "x", RowVersion = dto.RowVersion }, 1, null, CancellationToken.None));
        Assert.Equal("The spare part usage is already reversed.", again.Message);
        Assert.Equal(10, s.Part(10).CurrentStock); // restored exactly once
    }

    // ------------------------------------------------------------------ list / lookups

    [Fact]
    public async Task List_FiltersAndValidation()
    {
        var s = Create();
        await s.Post(10, 1);
        await s.Post(11, 1, SparePartUsageMaintenanceType.MoldPm, 30);

        Assert.Single((await s.Service.GetAllAsync(new SparePartUsageListQuery { MaintenanceType = "Mold PM" }, CancellationToken.None)).Items);
        Assert.Single((await s.Service.GetAllAsync(new SparePartUsageListQuery { MachineId = 1 }, CancellationToken.None)).Items);
        Assert.Single((await s.Service.GetAllAsync(new SparePartUsageListQuery { Search = "MPMD-0005" }, CancellationToken.None)).Items);
        await Assert.ThrowsAsync<ValidationException>(() => s.Service.GetAllAsync(new SparePartUsageListQuery { MaintenanceType = "Breakdown" }, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => s.Service.GetAllAsync(new SparePartUsageListQuery { DateFrom = s.Clock.Today, DateTo = s.Clock.Today.AddDays(-1) }, CancellationToken.None));
    }

    [Fact]
    public async Task Lookups_OnlyActiveParts_OnlyOpenAndDuePms()
    {
        var s = Create();
        var l = await s.Service.GetLookupsAsync(CancellationToken.None);

        Assert.Equal(new[] { 10, 11 }, l.SpareParts.Select(p => p.SparePartId));
        Assert.Equal(new[] { "MPM-0010" }, l.MachinePms.Select(p => p.MaintenanceNo));
        Assert.Equal(new[] { "MPMD-0005" }, l.MoldPms.Select(p => p.MaintenanceNo));
    }

    [Fact]
    public async Task AuditActions_FitTheColumn()
    {
        var s = Create();
        var (dto, _) = await s.Post(11, 5);
        await s.Service.ReverseAsync(dto.SparePartUsageId, new ReverseSparePartUsageRequest { Reason = "x", RowVersion = dto.RowVersion }, 1, null, CancellationToken.None);
        Assert.All(s.Audit.Entries, e => Assert.True(e.Action.Length <= 30));
    }
}

/// <summary>The pure stock rules - they must match the computed stock_status column exactly.</summary>
public class SparePartStockRulesTests
{
    [Theory]
    [InlineData(0, 5, "Out of Stock")]
    [InlineData(-1, 5, "Out of Stock")]
    [InlineData(5, 5, "Low Stock")]
    [InlineData(1, 5, "Low Stock")]
    [InlineData(6, 5, "Available")]
    [InlineData(0, 0, "Out of Stock")]
    [InlineData(1, 0, "Available")]
    public void StatusOf_MatchesTheComputedColumn(int current, int minimum, string expected) =>
        Assert.Equal(expected, SparePartStockRules.StatusOf(current, minimum));

    [Theory]
    [InlineData(10, 3, true)]
    [InlineData(5, 5, true)]
    [InlineData(2, 5, false)]
    [InlineData(5, 0, false)]
    public void CanIssue_NeverNegative(int stock, int qty, bool ok) => Assert.Equal(ok, SparePartStockRules.CanIssue(stock, qty));

    [Theory]
    [InlineData(10, 8, 5, null)]
    [InlineData(6, 4, 5, "Low Stock")]
    [InlineData(4, 3, 5, null)]
    [InlineData(3, 0, 5, "Out of Stock")]
    [InlineData(10, 0, 5, "Out of Stock")]
    [InlineData(4, 10, 5, null)] // a reversal back to Available notifies nothing
    public void AlertTransition_OnlyIntoLowOrOut(int before, int after, int min, string? expected) =>
        Assert.Equal(expected, SparePartStockRules.AlertTransition(before, after, min));
}

/// <summary>The Spare Part master now explains its stock changes in the ledger too.</summary>
public class SparePartMasterLedgerTests
{
    private static (SparePartService Service, InMemorySparePartRepository Repo) Create()
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machines = new InMemoryMachineRepository(MachineTestData.Machines(), departments, employees);
        var vendors = new InMemoryVendorRepository(VendorTestData.Vendors());
        var parts = new InMemorySparePartRepository(SparePartTestData.SpareParts(), machines, vendors);
        return (new SparePartService(parts, machines, vendors, users, new FixedClock(), new RecordingAuditLog(), NullLogger<SparePartService>.Instance), parts);
    }

    [Fact]
    public async Task Create_WritesAnOpeningRow_Update_WritesAnAdjustment_OnlyWhenStockChanges()
    {
        var (service, repo) = Create();
        var created = await service.CreateAsync(new CreateSparePartRequest { SparePartName = "Ledger Part", Unit = "Nos", MinimumStock = 1, CurrentStock = 12 }, 1, null, CancellationToken.None);

        var opening = Assert.Single(repo.Ledger);
        Assert.Equal((SparePartStockTransactionType.Opening, 12, 0, 12, created.SparePartCode), (opening.TransactionType, opening.Quantity, opening.PreviousStock, opening.NewStock, opening.ReferenceNo));

        UpdateSparePartRequest Req(int stock, string rv) => new() { SparePartName = "Ledger Part", Unit = "Nos", MinimumStock = 1, CurrentStock = stock, RowVersion = rv };
        var same = await service.UpdateAsync(created.SparePartId, Req(12, created.RowVersion), 1, null, CancellationToken.None);
        Assert.Single(repo.Ledger); // no stock change -> no ledger row

        await service.UpdateAsync(created.SparePartId, Req(9, same.RowVersion), 1, null, CancellationToken.None);
        var adjustment = repo.Ledger.Last();
        Assert.Equal((SparePartStockTransactionType.Adjustment, -3, 12, 9), (adjustment.TransactionType, adjustment.Quantity, adjustment.PreviousStock, adjustment.NewStock));
    }
}
