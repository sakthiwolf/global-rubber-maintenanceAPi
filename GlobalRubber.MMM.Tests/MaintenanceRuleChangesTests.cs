using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Domain.Rules;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The maintenance-rule changes approved 2026-11-18:
///   A1 - Machine PM "Start Maintenance": In Progress, machine under Maintenance; completion releases it only when no other
///        PM of the machine is still in progress; a machine in Breakdown stays in Breakdown.
///   A2 - the mold status is system-managed (only Retire / Reactivate by hand).
///   A3 - production puts an Available mold In Production.
///   A4 - a wrong production entry is cancelled: its shots come back off the mold.
///   A7 - an unticked check point needs Remarks.
/// </summary>
public class MaintenanceRuleChangesTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    // ================================================================ Machine PM (A1, A7)

    private sealed record PmSut(MachinePmService Service, InMemoryMachinePmRepository Repo, RecordingAuditLog Audit);

    private static PmSut CreatePm(MachinePmScenario data, DateOnly today)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var repo = new InMemoryMachinePmRepository(data);
        var audit = new RecordingAuditLog();
        var service = new MachinePmService(repo, users, new SettableClock(today), audit, new RecordingNotificationPublisher(), NullLogger<MachinePmService>.Instance);
        return new PmSut(service, repo, audit);
    }

    private static StartMachinePmRequest StartReq(PmSut s, MachinePm pm) =>
        new() { RowVersion = Convert.ToBase64String(s.Repo.Stored(pm.MachinePmId).RowVersion) };

    private static CompleteMachinePmRequest AllTicked(PmSut s, MachinePm pm, string? remarks = null)
    {
        var stored = s.Repo.Stored(pm.MachinePmId);
        return new CompleteMachinePmRequest
        {
            MaintenanceBy = "Ravi", Remarks = remarks, RowVersion = Convert.ToBase64String(stored.RowVersion),
            Results = stored.ChecklistItems.Select(i => new MachinePmChecklistResultRequest { MachinePmChecklistId = i.MachinePmChecklistId, IsChecked = true }).ToList(),
        };
    }

    [Fact]
    public async Task Start_MakesThePmInProgress_AndPutsARunningMachineUnderMaintenance_AndAudits()
    {
        var data = new MachinePmScenario();
        var pm = data.Open(data.Checklist("Monthly", D(2026, 11, 1)), D(2026, 11, 18));
        var s = CreatePm(data, D(2026, 11, 18));

        var dto = await s.Service.StartAsync(pm.MachinePmId, StartReq(s, pm), 1, null, CancellationToken.None);

        Assert.Equal(MachinePmStatus.InProgress, dto.Status);
        Assert.Equal(MachinePmStatus.InProgress, s.Repo.Stored(pm.MachinePmId).Status);
        Assert.Equal(MachineOperationalStatus.Maintenance, s.Repo.StoredMachine(1).OperationalStatus);
        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("MachinePmStarted", entry.Action);
        Assert.Contains(entry.Details, d => d is { FieldName: "machine_operational_status (MAC-0001)", OldValue: "Running", NewValue: "Maintenance" });
    }

    [Fact]
    public async Task Start_TwiceIs409_AStaleRowVersionIs409()
    {
        var data = new MachinePmScenario();
        var pm = data.Open(data.Checklist("Monthly", D(2026, 11, 1)), D(2026, 11, 18));
        var s = CreatePm(data, D(2026, 11, 18));
        var stale = new StartMachinePmRequest { RowVersion = "AQ==" };

        await s.Service.StartAsync(pm.MachinePmId, StartReq(s, pm), 1, null, CancellationToken.None);

        var again = await Assert.ThrowsAsync<ConflictException>(() => s.Service.StartAsync(pm.MachinePmId, StartReq(s, pm), 1, null, CancellationToken.None));
        Assert.Contains("already been started", again.Message);

        var other = data.Open(data.Checklist("Weekly", D(2026, 11, 1)), D(2026, 11, 18));
        s.Repo.SimulateConcurrentModification(other.MachinePmId);
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.StartAsync(other.MachinePmId, stale, 1, null, CancellationToken.None));
        Assert.Equal(MachinePmStatus.Scheduled, s.Repo.Stored(other.MachinePmId).Status);
    }

    [Fact]
    public async Task Start_OnAMachineInBreakdown_KeepsBreakdown()
    {
        var data = new MachinePmScenario();
        var pm = data.Open(data.Checklist("Monthly", D(2026, 11, 1), machineId: 2), D(2026, 11, 18));
        var s = CreatePm(data, D(2026, 11, 18));

        await s.Service.StartAsync(pm.MachinePmId, StartReq(s, pm), 1, null, CancellationToken.None);

        Assert.Equal(MachineOperationalStatus.Breakdown, s.Repo.StoredMachine(2).OperationalStatus);
    }

    [Fact]
    public async Task Complete_ReleasesTheMachine_OnlyWhenNoOtherPmIsStillInProgress()
    {
        var data = new MachinePmScenario();
        var monthly = data.Open(data.Checklist("Monthly", D(2026, 11, 1)), D(2026, 11, 18));
        var weekly = data.Open(data.Checklist("Weekly", D(2026, 11, 1)), D(2026, 11, 18));
        var s = CreatePm(data, D(2026, 11, 18));

        await s.Service.StartAsync(monthly.MachinePmId, StartReq(s, monthly), 1, null, CancellationToken.None);
        await s.Service.StartAsync(weekly.MachinePmId, StartReq(s, weekly), 1, null, CancellationToken.None);

        await s.Service.CompleteAsync(monthly.MachinePmId, AllTicked(s, monthly), 1, null, CancellationToken.None);
        Assert.Equal(MachineOperationalStatus.Maintenance, s.Repo.StoredMachine(1).OperationalStatus); // weekly still running

        await s.Service.CompleteAsync(weekly.MachinePmId, AllTicked(s, weekly), 1, null, CancellationToken.None);
        Assert.Equal(MachineOperationalStatus.Running, s.Repo.StoredMachine(1).OperationalStatus);
    }

    [Fact]
    public async Task Complete_WithAnUntickedPoint_NeedsRemarks()
    {
        var data = new MachinePmScenario();
        var pm = data.Open(data.Checklist("Monthly", D(2026, 11, 1)), D(2026, 11, 18));
        var s = CreatePm(data, D(2026, 11, 18));
        var oneTicked = new List<MachinePmChecklistResultRequest> { new() { MachinePmChecklistId = pm.ChecklistItems[0].MachinePmChecklistId, IsChecked = true } };
        var rv = Convert.ToBase64String(pm.RowVersion);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CompleteAsync(pm.MachinePmId,
            new CompleteMachinePmRequest { MaintenanceBy = "Ravi", RowVersion = rv, Results = oneTicked }, 1, null, CancellationToken.None));
        Assert.Contains("1 checklist item(s) are not ticked. Tick them, or enter Remarks explaining why they were not done.", ex.Errors);
        Assert.Equal(MachinePmStatus.Scheduled, s.Repo.Stored(pm.MachinePmId).Status);

        var dto = await s.Service.CompleteAsync(pm.MachinePmId,
            new CompleteMachinePmRequest { MaintenanceBy = "Ravi", RowVersion = rv, Results = oneTicked, Remarks = "guard part on order" }, 1, null, CancellationToken.None);
        Assert.Equal(MachinePmStatus.Completed, dto.Status);
    }

    [Theory]
    [InlineData(MachineOperationalStatus.Running, MachineOperationalStatus.Maintenance)]
    [InlineData(MachineOperationalStatus.Idle, MachineOperationalStatus.Maintenance)]
    [InlineData(MachineOperationalStatus.Breakdown, MachineOperationalStatus.Breakdown)]
    [InlineData(MachineOperationalStatus.Maintenance, MachineOperationalStatus.Maintenance)]
    public void OperationalStatusAfterStart(string before, string after) =>
        Assert.Equal(after, MachinePmRules.OperationalStatusAfterStart(before));

    // ================================================================ Mold status (A2, A3)

    [Theory]
    [InlineData(null, null, 0, MoldStatus.Available)]                          // a new mold
    [InlineData(null, MoldStatus.Maintenance, 250_000, MoldStatus.InProduction)] // a manual status is ignored
    [InlineData(null, MoldStatus.Retired, 0, MoldStatus.Retired)]
    [InlineData(null, null, 500_000, MoldStatus.ReplacementDue)]
    [InlineData(MoldStatus.InProduction, MoldStatus.Available, 120_000, MoldStatus.InProduction)]
    [InlineData(MoldStatus.ReplacementDue, null, 120_000, MoldStatus.InProduction)] // limit raised / usage corrected
    [InlineData(MoldStatus.Maintenance, null, 120_000, MoldStatus.Maintenance)]     // a PM in progress decides
    [InlineData(MoldStatus.Retired, null, 120_000, MoldStatus.Retired)]             // stays retired
    [InlineData(MoldStatus.Retired, MoldStatus.Available, 120_000, MoldStatus.InProduction)] // reactivated
    [InlineData(MoldStatus.InProduction, MoldStatus.Retired, 120_000, MoldStatus.Retired)]
    public void StatusAfterMasterSave(string? current, string? requested, int usage, string expected) =>
        Assert.Equal(expected, MoldLifeEngine.StatusAfterMasterSave(current, requested, usage, 500_000));

    // ================================================================ Production entry cancel (A4)

    private sealed record ProdSut(ProductionEntryService Service, InMemoryProductionEntryRepository Entries, RecordingAuditLog Audit);

    private static ProdSut CreateProd()
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
        var evaluator = MoldPmTestFactory.Evaluator(new InMemoryMoldPmRepository(moldList), new InMemoryNotificationRepository(), new FixedClock(), audit);
        var service = new ProductionEntryService(entries, machines, products, molds, evaluator, users, new FixedClock(), audit, NullLogger<ProductionEntryService>.Instance);
        return new ProdSut(service, entries, audit);
    }

    private static CreateProductionEntryRequest Entry(int qty) =>
        new() { EntryDate = D(2026, 9, 25), Shift = "Shift A", MachineId = 1, ProductId = 1, MoldId = 1, ProductionQty = qty, RejectedQty = 0 };

    [Fact]
    public async Task Cancel_TakesTheEntrysShotsBackOffTheMold_KeepsLaterEntries_AndKeepsTheEntryAsCancelled()
    {
        var s = CreateProd();
        var start = s.Entries.StoredMold(1).CurrentUsageShots;
        var wrong = await s.Service.CreateAsync(Entry(6_560), 1, null, CancellationToken.None);
        await s.Service.CreateAsync(Entry(656), 1, null, CancellationToken.None);

        var dto = await s.Service.CancelAsync(wrong.ProductionEntryId,
            new CancelProductionEntryRequest { Reason = "  typo - should be 656  ", RowVersion = wrong.RowVersion }, 1, null, CancellationToken.None);

        Assert.Equal(ProductionEntryStatus.Cancelled, dto.Status);
        Assert.Contains("Cancelled: typo - should be 656", dto.Remarks);
        Assert.Equal(start + 656, s.Entries.StoredMold(1).CurrentUsageShots);
        var audit = s.Audit.Entries.Last();
        Assert.Equal("ProductionEntryCancelled", audit.Action);
        Assert.Contains(audit.Details, d => d is { FieldName: "status", OldValue: "Saved", NewValue: "Cancelled" });
    }

    [Fact]
    public async Task Cancel_NeedsAReason_AndTwiceIs409()
    {
        var s = CreateProd();
        var entry = await s.Service.CreateAsync(Entry(1_000), 1, null, CancellationToken.None);

        var noReason = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CancelAsync(entry.ProductionEntryId,
            new CancelProductionEntryRequest { Reason = " ", RowVersion = entry.RowVersion }, 1, null, CancellationToken.None));
        Assert.Contains("Reason is required.", noReason.Errors);

        var cancelled = await s.Service.CancelAsync(entry.ProductionEntryId,
            new CancelProductionEntryRequest { Reason = "duplicate", RowVersion = entry.RowVersion }, 1, null, CancellationToken.None);
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.CancelAsync(entry.ProductionEntryId,
            new CancelProductionEntryRequest { Reason = "duplicate", RowVersion = cancelled.RowVersion }, 1, null, CancellationToken.None));
    }

    [Fact]
    public async Task Cancel_OfTheEntryThatHitTheLimit_MakesTheMoldUsableAgain()
    {
        var s = CreateProd();
        var mold = s.Entries.StoredMold(1);
        mold.CurrentUsageShots = mold.ReplacementShots - 10;
        var entry = await s.Service.CreateAsync(Entry(100), 1, null, CancellationToken.None);
        Assert.Equal(MoldStatus.ReplacementDue, mold.Status);

        await s.Service.CancelAsync(entry.ProductionEntryId, new CancelProductionEntryRequest { Reason = "wrong mold", RowVersion = entry.RowVersion }, 1, null, CancellationToken.None);

        Assert.Equal((mold.ReplacementShots - 10, MoldStatus.InProduction), (mold.CurrentUsageShots, mold.Status));
    }
}
