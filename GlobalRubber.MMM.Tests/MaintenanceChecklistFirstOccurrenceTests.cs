using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Domain.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Function 3: creating an active MACHINE checklist creates its first Machine PM occurrence in the same transaction.
/// The fake mirrors the live data: MPM-0001..MPM-0005 completed and MPM-0006 OPEN on machine 1 (from the inactive legacy
/// checklist 3), so the next number is MPM-0007. Plant "today" = FixedClock.Today (2026-03-01).
/// Machines: 1 MAC-0001 (active, has the legacy open occurrence), 2 MAC-0002 (active, no occurrence), 3 inactive.
/// </summary>
public class MaintenanceChecklistFirstOccurrenceTests
{
    private static readonly DateOnly Today = new FixedClock().Today;
    private static readonly DateOnly LegacyDue = new(2026, 2, 25);

    private sealed record Sut(MaintenanceChecklistService Service, InMemoryMaintenanceChecklistRepository Repo, RecordingAuditLog Audit);

    private static List<MachinePm> LivePms()
    {
        var pms = Enumerable.Range(1, 5).Select(n => new MachinePm
        {
            MachinePmId = n, PmNo = $"MPM-{n:0000}", MachineId = 1, MaintenanceTypeId = 19, ChecklistId = 3, EngineerId = 3,
            ScheduledDate = new DateOnly(2026, 2, 20), CompletedDate = new DateOnly(2026, 2, 20), Status = MachinePmStatus.Completed,
            RowVersion = new byte[] { 1 },
        }).ToList();
        pms.Add(new MachinePm
        {
            MachinePmId = 6, PmNo = "MPM-0006", MachineId = 1, MaintenanceTypeId = 19, ChecklistId = 3, EngineerId = 3,
            ScheduledDate = LegacyDue, Status = MachinePmStatus.Scheduled, RowVersion = new byte[] { 7 },
        });
        return pms;
    }

    private static Sut Create()
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machineList = MachineTestData.Machines();
        var repo = new InMemoryMaintenanceChecklistRepository(MaintenanceChecklistTestData.Checklists(), machineList, LivePms());
        var audit = new RecordingAuditLog();
        var service = new MaintenanceChecklistService(repo, new InMemoryMachineRepository(machineList, departments, employees), users,
            new FixedClock(), audit, NullLogger<MaintenanceChecklistService>.Instance);
        return new Sut(service, repo, audit);
    }

    private static CreateMaintenanceChecklistRequest New(string appliesTo = "Machine", string frequency = "Daily", int? machineId = 2, string name = "Injection Machine Daily Inspection", DateOnly? startDate = null) =>
        new()
        {
            ChecklistName = name, AppliesTo = appliesTo, Frequency = frequency, MachineId = machineId, StartDate = startDate ?? Today,
            Items = new[] { "Oil level checked", "", "Hydraulic pressure checked", "Electrical panel checked" }.Select(l => new MaintenanceChecklistItemRequest { ItemLabel = l }).ToList(),
        };

    private static MachinePm NewOccurrence(Sut s) => s.Repo.Pms.Single(p => p.PmNo == "MPM-0007");

    private static string Fingerprint(MachinePm p) =>
        $"{p.PmNo}|{p.MachineId}|{p.ChecklistId}|{p.MaintenanceTypeId}|{p.EngineerId}|{p.ScheduledDate}|{p.CompletedDate}|{p.Status}|{p.MaintenanceBy}|{p.Remarks}|{p.RowVersion[0]}";

    // ================================================================ the occurrence (1 - 16)

    [Fact]
    public async Task CreatingAnActiveMachineChecklist_CreatesExactlyOneOccurrence_WithTheApprovedFields()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(New(), 1, "10.0.0.5", CancellationToken.None);

        var created = Assert.Single(s.Repo.Pms, p => p.ChecklistId == dto.ChecklistId);     // 1
        Assert.Equal(7, s.Repo.Pms.Count);
        Assert.Equal(2, created.MachineId);                                                   // 2 the selected machine
        Assert.Equal(dto.ChecklistId, created.ChecklistId);                                   // 3 the new checklist
        Assert.Equal(Today, created.ScheduledDate);                                           // 4 first cycle date on/after today
        Assert.Equal(MachinePmStatus.Scheduled, created.Status);                              // 5
        Assert.Null(created.MaintenanceTypeId);                                               // 6
        Assert.Null(created.EngineerId);                                                      // 7
        Assert.Null(created.MaintenanceBy);                                                   // 8
        Assert.Null(created.CompletedDate);                                                   // 9
        Assert.Null(created.Remarks);
        Assert.Equal("MPM-0007", created.PmNo);                                               // 10 next MACHINE_PM number
        Assert.Equal(7, s.Repo.PmSequence);
        Assert.Equal((FixedClock.Now, (int?)1), (created.CreatedAt, created.CreatedBy));
        Assert.Null(created.UpdatedAt);
        Assert.Null(created.UpdatedBy);
    }

    [Fact]
    public async Task TheOccurrenceGetsASnapshotOfTheChecklistItems()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(New(), 1, null, CancellationToken.None);

        var lines = NewOccurrence(s).ChecklistItems;
        Assert.Equal(3, lines.Count);                                                                                    // 11 blank row dropped
        Assert.Equal(new[] { 1, 2, 3 }, lines.Select(l => l.SortOrder));                                                 // 12
        Assert.Equal(new[] { "Oil level checked", "Hydraulic pressure checked", "Electrical panel checked" }, lines.Select(l => l.ItemLabel)); // 13
        Assert.Equal(dto.Items.Select(i => (int?)i.ChecklistItemId), lines.Select(l => l.ChecklistItemId));              // 14 master item ids
        Assert.All(lines, l => Assert.False(l.IsChecked));                                                               // 15
    }

    [Fact]
    public async Task TheSnapshotIsACopy_NotALiveReferenceToTheMasterLabels()
    {
        var s = Create();
        var dto = await s.Service.CreateAsync(New(), 1, null, CancellationToken.None);

        s.Repo.Stored(dto.ChecklistId).Items[0].ItemLabel = "Oil level checked (NEW WORDING)"; // the master changes later

        Assert.Equal("Oil level checked", NewOccurrence(s).ChecklistItems[0].ItemLabel);
    }

    [Theory]
    [InlineData("Daily")]
    [InlineData("Weekly")]
    [InlineData("Monthly")]
    [InlineData("Yearly")]
    public async Task TheFirstOccurrenceIsDueToday_ForEveryFrequency_BecauseTheAnchorIsTheCreationDate(string frequency)
    {
        var s = Create();

        await s.Service.CreateAsync(New(frequency: frequency), 1, null, CancellationToken.None);

        var anchor = GlobalRubber.MMM.Domain.Common.PlantTime.ToPlantDate(FixedClock.Now);
        Assert.Equal(RecurrenceRules.FirstOnOrAfter(anchor, frequency, Today), NewOccurrence(s).ScheduledDate);
        Assert.Equal(Today, NewOccurrence(s).ScheduledDate);
    }

    [Fact] // 16
    public async Task TheMachineNextDate_BecomesTheOccurrenceDate_WhenItIsTheMachinesOnlyOpenOccurrence()
    {
        var s = Create();

        await s.Service.CreateAsync(New(machineId: 2), 1, null, CancellationToken.None);

        Assert.Equal(Today, s.Repo.StoredMachine(2).NextMaintenanceDate);
        Assert.Equal((FixedClock.Now, (int?)1), (s.Repo.StoredMachine(2).UpdatedAt!.Value, s.Repo.StoredMachine(2).UpdatedBy));
    }

    [Fact] // 16 (approved rule: the EARLIEST open occurrence of the machine)
    public async Task TheMachineNextDate_IsTheEarliestOpenOccurrence_IncludingOlderOpenOnes()
    {
        var s = Create();

        await s.Service.CreateAsync(New(machineId: 1), 1, null, CancellationToken.None);

        Assert.Equal(LegacyDue, s.Repo.StoredMachine(1).NextMaintenanceDate); // MPM-0006 (25-Feb) is earlier than today's new one
    }

    // ================================================================ atomicity (17 - 20)

    [Fact] // 17
    public async Task ChecklistAndOccurrence_AreCommittedTogether()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(New(), 1, null, CancellationToken.None);

        Assert.Equal(("CHK-0004", 4), (dto.ChecklistCode, s.Repo.Count));
        Assert.Equal((7, 7), (s.Repo.Pms.Count, s.Repo.PmSequence));
    }

    [Fact] // 18 + 19
    public async Task AFailureWhileWritingTheOccurrence_RollsBackTheChecklist_AndConsumesNoNumber()
    {
        var s = Create();
        s.Repo.FailAtOccurrenceInsert = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => s.Service.CreateAsync(New(), 1, null, CancellationToken.None));

        AssertNothingChanged(s);
        var retry = await s.Service.CreateAsync(New(), 1, null, CancellationToken.None);
        Assert.Equal(("CHK-0004", "MPM-0007"), (retry.ChecklistCode, s.Repo.Pms.Last().PmNo)); // no number was burnt
    }

    [Fact] // 20
    public async Task AFailureWhileUpdatingTheMachine_RollsBackEverything()
    {
        var s = Create();
        s.Repo.FailAtMachineUpdate = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => s.Service.CreateAsync(New(), 1, null, CancellationToken.None));

        AssertNothingChanged(s);
    }

    // ================================================================ existing data (21, 22)

    [Fact]
    public async Task TheLegacyOpenOccurrence_AndTheInactiveLegacyChecklist_AreUntouched()
    {
        var s = Create();
        var legacy = Fingerprint(s.Repo.Pms.Single(p => p.PmNo == "MPM-0006"));
        var inactive = s.Repo.Stored(3);
        var inactiveBefore = (inactive.ChecklistName, inactive.IsActive, inactive.Frequency, inactive.MachineId, inactive.RowVersion[0]);

        await s.Service.CreateAsync(New(machineId: 1), 1, null, CancellationToken.None);        // same machine as MPM-0006

        Assert.Equal(legacy, Fingerprint(s.Repo.Pms.Single(p => p.PmNo == "MPM-0006")));           // 21
        var after = s.Repo.Stored(3);
        Assert.Equal(inactiveBefore, (after.ChecklistName, after.IsActive, after.Frequency, after.MachineId, after.RowVersion[0])); // 22
    }

    // ================================================================ no occurrence (23, 24)

    [Fact] // 23
    public async Task AMoldChecklist_CreatesNoMachinePm_AndTouchesNoMachine()
    {
        var s = Create();
        var machineNext = s.Repo.StoredMachine(1).NextMaintenanceDate;

        await s.Service.CreateAsync(New(appliesTo: "Mold", machineId: null, name: "Mold Clean"), 1, null, CancellationToken.None);

        Assert.Equal((6, 6), (s.Repo.Pms.Count, s.Repo.PmSequence));
        Assert.Equal(machineNext, s.Repo.StoredMachine(1).NextMaintenanceDate);
        Assert.Equal("ChecklistCreated", Assert.Single(s.Audit.Entries).Action);
    }

    [Fact] // 24 + 27
    public async Task InvalidRequests_StillFailValidation_AndLeaveNoBusinessOrAuditData()
    {
        var s = Create();

        await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(New(machineId: 3), 1, null, CancellationToken.None));     // inactive machine
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(New(machineId: 99), 1, null, CancellationToken.None));       // unknown machine
        await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(New(frequency: "Hourly"), 1, null, CancellationToken.None));
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateAsync(New(name: "Machine Lubrication"), 1, null, CancellationToken.None)); // duplicate name

        Assert.Equal(0, s.Repo.AddCalls); // refused before the transaction
        AssertNothingChanged(s);
    }

    // ================================================================ audit (26)

    [Fact]
    public async Task SuccessfulCreation_AuditsChecklistCreated_ThenMachinePmScheduled()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(New(machineId: 2), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal(new[] { "ChecklistCreated", "MachinePmScheduled" }, s.Audit.Entries.Select(e => e.Action));
        var pm = s.Audit.Entries[1];
        Assert.Equal(("Machine Preventive Maintenance", "MachinePm", "MPM-0007"), (pm.Module, pm.EntityName, pm.RecordRef));
        Assert.Equal(NewOccurrence(s).MachinePmId, pm.EntityId);
        Assert.Equal(("Sakthi", "10.0.0.5", (int?)1), (pm.UserName, pm.IpAddress, pm.UserId));
        Assert.Equal($"Sakthi scheduled machine PM MPM-0007 for machine MAC-0002 on 2026-03-01 from daily checklist {dto.ChecklistCode} (3 items).", pm.Description);
        Assert.Contains(pm.Details, d => d is { FieldName: "machine_next_maintenance_date", NewValue: "2026-03-01" });
        Assert.All(s.Audit.Entries, e => Assert.True(e.Action.Length <= 30));
    }

    [Fact]
    public async Task WhenTheAuditWriteFails_TheChecklistAndOccurrenceStillExist()
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machineList = MachineTestData.Machines();
        var repo = new InMemoryMaintenanceChecklistRepository(MaintenanceChecklistTestData.Checklists(), machineList, LivePms());
        var failingAudit = new AuditLogService(new ThrowingAuditRepository(), new FixedClock(), NullLogger<AuditLogService>.Instance);
        var service = new MaintenanceChecklistService(repo, new InMemoryMachineRepository(machineList, departments, employees),
            new InMemoryUserRepository(UserTestData.Users(), roles.Find), new FixedClock(), failingAudit, NullLogger<MaintenanceChecklistService>.Instance);

        var dto = await service.CreateAsync(New(), 1, null, CancellationToken.None);

        Assert.Equal("CHK-0004", dto.ChecklistCode);
        Assert.Contains(repo.Pms, p => p.PmNo == "MPM-0007");
    }

    private static void AssertNothingChanged(Sut s)
    {
        Assert.Equal(3, s.Repo.Count);                                  // no checklist
        Assert.Equal((6, 6), (s.Repo.Pms.Count, s.Repo.PmSequence));     // no PM, no MACHINE_PM number
        Assert.Null(s.Repo.StoredMachine(2).UpdatedBy);                  // machine untouched
        Assert.Empty(s.Audit.Entries);                                   // no audit
    }

    private sealed class ThrowingAuditRepository : GlobalRubber.MMM.Application.Interfaces.Repositories.IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit persistence failure.");
    }
}
