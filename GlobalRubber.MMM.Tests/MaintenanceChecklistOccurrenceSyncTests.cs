using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Function 4: editing / deactivating a checklist keeps its OPEN Machine PM occurrence in step - snapshot refresh, frequency
/// re-dating, machine move, Machine &lt;-&gt; Mold - in one transaction; completed history is never touched.
/// Setup: live-like PMs MPM-0001..0006 (MPM-0006 OPEN on machine 1, due 2026-02-25, legacy inactive checklist 3); then the
/// test creates "Press Daily" (Daily, machine 2) on 2026-09-25 -&gt; its open occurrence MPM-0007 due 2026-09-25, plus a
/// seeded COMPLETED history occurrence MPM-0050 of the same checklist. The clock is then moved to 2026-09-29.
/// </summary>
public class MaintenanceChecklistOccurrenceSyncTests
{
    private static readonly DateOnly Anchor = new(2026, 9, 25);
    private static readonly DateOnly Edit29 = new(2026, 9, 29);
    private static readonly DateOnly LegacyDue = new(2026, 2, 25);

    private sealed class SettableClock : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = new(2026, 9, 25, 5, 0, 0, DateTimeKind.Utc); // 10:30 IST on the 25th
        public DateOnly Today => GlobalRubber.MMM.Domain.Common.PlantTime.ToPlantDate(UtcNow);
    }

    private sealed record Sut(MaintenanceChecklistService Service, InMemoryMaintenanceChecklistRepository Repo, RecordingAuditLog Audit, SettableClock Clock, MaintenanceChecklistDto Press);

    private static async Task<Sut> CreateAsync(string frequency = "Daily", string appliesTo = "Machine", int? machineId = 2)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machineList = MachineTestData.Machines();
        var pms = Enumerable.Range(1, 5).Select(n => new MachinePm
        {
            MachinePmId = n, PmNo = $"MPM-{n:0000}", MachineId = 1, MaintenanceTypeId = 19, ChecklistId = 3, EngineerId = 3,
            ScheduledDate = LegacyDue, CompletedDate = LegacyDue, Status = MachinePmStatus.Completed, RowVersion = new byte[] { 1 },
        }).ToList();
        pms.Add(new MachinePm { MachinePmId = 6, PmNo = "MPM-0006", MachineId = 1, MaintenanceTypeId = 19, ChecklistId = 3, EngineerId = 3, ScheduledDate = LegacyDue, Status = MachinePmStatus.Scheduled, RowVersion = new byte[] { 7 } });
        var repo = new InMemoryMaintenanceChecklistRepository(MaintenanceChecklistTestData.Checklists(), machineList, pms);
        var audit = new RecordingAuditLog();
        var clock = new SettableClock();
        var service = new MaintenanceChecklistService(repo, new InMemoryMachineRepository(machineList, departments, employees), users, clock, audit, NullLogger<MaintenanceChecklistService>.Instance);

        var press = await service.CreateAsync(new CreateMaintenanceChecklistRequest
        {
            ChecklistName = "Press Daily", AppliesTo = appliesTo, Frequency = frequency, MachineId = machineId, StartDate = clock.Today, // 25-Sep
            Items = Labels("Oil level checked", "Hydraulic pressure checked", "Safety guard checked"),
        }, 1, null, CancellationToken.None);

        if (appliesTo == "Machine")
        {
            // Completed history of the same checklist (Function 5 will create these; seeded here).
            repo.Pms.Add(new MachinePm
            {
                MachinePmId = 50, PmNo = "MPM-0050", MachineId = 2, ChecklistId = press.ChecklistId, ScheduledDate = new DateOnly(2026, 9, 20),
                CompletedDate = new DateOnly(2026, 9, 20), MaintenanceBy = "Ravi", Status = MachinePmStatus.Completed, RowVersion = new byte[] { 3 },
                ChecklistItems = press.Items.Select(i => new MachinePmChecklistItem { MachinePmChecklistId = 900 + i.SortOrder, MachinePmId = 50, SortOrder = i.SortOrder, ItemLabel = i.ItemLabel, ChecklistItemId = i.ChecklistItemId, IsChecked = true }).ToList(),
            });
        }

        audit.Entries.Clear();
        clock.UtcNow = new DateTime(2026, 9, 29, 5, 0, 0, DateTimeKind.Utc); // the edits happen on the 29th
        return new Sut(service, repo, audit, clock, press);
    }

    private static List<MaintenanceChecklistItemRequest> Labels(params string[] labels) => labels.Select(l => new MaintenanceChecklistItemRequest { ItemLabel = l }).ToList();

    private static UpdateMaintenanceChecklistRequest Edit(Sut s, string? appliesTo = null, string? frequency = "(keep)", int? machineId = -1, string[]? items = null, string? rowVersion = null, string? name = null, DateOnly? startDate = null)
    {
        var stored = s.Repo.Stored(s.Press.ChecklistId);
        return new UpdateMaintenanceChecklistRequest
        {
            ChecklistName = name ?? stored.ChecklistName,
            AppliesTo = appliesTo ?? stored.AppliesTo,
            Frequency = frequency == "(keep)" ? stored.Frequency : frequency,
            MachineId = machineId == -1 ? stored.MachineId : machineId,
            StartDate = startDate ?? stored.StartDate,
            Items = Labels(items ?? stored.Items.OrderBy(i => i.SortOrder).Select(i => i.ItemLabel).ToArray()),
            RowVersion = rowVersion ?? Convert.ToBase64String(stored.RowVersion),
        };
    }

    private static Task<MaintenanceChecklistDto> Update(Sut s, UpdateMaintenanceChecklistRequest request) =>
        s.Service.UpdateAsync(s.Press.ChecklistId, request, 1, "10.0.0.5", CancellationToken.None);

    private static MachinePm Open(Sut s) => s.Repo.Pms.Single(p => p.ChecklistId == s.Press.ChecklistId && p.Status == MachinePmStatus.Scheduled);

    private static string History(Sut s) => string.Join("|", s.Repo.StoredPm("MPM-0050").ChecklistItems.Select(l => $"{l.MachinePmChecklistId}:{l.SortOrder}:{l.ItemLabel}:{l.ChecklistItemId}:{l.IsChecked}"))
                                          + $"#{s.Repo.StoredPm("MPM-0050").ScheduledDate}:{s.Repo.StoredPm("MPM-0050").MachineId}:{s.Repo.StoredPm("MPM-0050").RowVersion[0]}";

    private static string Everything(Sut s) =>
        $"{s.Repo.Count}/{s.Repo.PmSequence}/" + string.Join(",", s.Repo.Pms.Select(p => $"{p.PmNo}:{p.MachineId}:{p.ScheduledDate}:{p.Status}:{string.Join(";", p.ChecklistItems.Select(l => l.ItemLabel))}"))
        + $"/m1={s.Repo.StoredMachine(1).NextMaintenanceDate}/m2={s.Repo.StoredMachine(2).NextMaintenanceDate}"
        + $"/{s.Repo.Stored(s.Press.ChecklistId).ChecklistName}:{s.Repo.Stored(s.Press.ChecklistId).Frequency}:{s.Repo.Stored(s.Press.ChecklistId).MachineId}:{s.Repo.Stored(s.Press.ChecklistId).RowVersion[0]}:"
        + string.Join(";", s.Repo.Stored(s.Press.ChecklistId).Items.Select(i => i.ItemLabel));

    [Fact]
    public async Task Setup_TheChecklistHasOneOpenOccurrence_MPM0007_DueOnTheAnchor()
    {
        var s = await CreateAsync();

        Assert.Equal(("MPM-0007", 2, Anchor), (Open(s).PmNo, Open(s).MachineId, Open(s).ScheduledDate));
        Assert.Equal(Anchor, s.Repo.StoredMachine(2).NextMaintenanceDate);
    }

    // ================================================================ A, B: items

    [Fact] // A
    public async Task ItemLabelChanged_RefreshesTheOpenSnapshot_CompletedHistoryUntouched()
    {
        var s = await CreateAsync();
        var history = History(s);

        var dto = await Update(s, Edit(s, items: new[] { "Oil level checked", "Hydraulic pressure checked (bar)", "Safety guard checked" }));

        var open = Open(s);
        Assert.Equal("MPM-0007", open.PmNo); // same occurrence
        Assert.Equal(new[] { "Oil level checked", "Hydraulic pressure checked (bar)", "Safety guard checked" }, open.ChecklistItems.Select(l => l.ItemLabel));
        Assert.Equal(dto.Items.Select(i => (int?)i.ChecklistItemId), open.ChecklistItems.Select(l => l.ChecklistItemId)); // new master ids
        Assert.All(open.ChecklistItems, l => Assert.False(l.IsChecked));
        Assert.Equal(Anchor, open.ScheduledDate); // frequency unchanged -> date unchanged
        Assert.Equal(history, History(s));
    }

    [Fact] // B
    public async Task ItemOrderChanged_ReordersTheOpenSnapshot_CompletedHistoryUntouched()
    {
        var s = await CreateAsync();
        var history = History(s);

        await Update(s, Edit(s, items: new[] { "Safety guard checked", "Oil level checked", "Hydraulic pressure checked" }));

        Assert.Equal(new[] { (1, "Safety guard checked"), (2, "Oil level checked"), (3, "Hydraulic pressure checked") }, Open(s).ChecklistItems.Select(l => (l.SortOrder, l.ItemLabel)));
        Assert.Equal(history, History(s));
    }

    [Fact]
    public async Task ANameOnlyEdit_TouchesNoOccurrence_AndLocksNoMachine()
    {
        var s = await CreateAsync();
        var open = Open(s);

        await Update(s, Edit(s, name: "Press Daily Inspection"));

        Assert.Same(open, Open(s)); // not even rewritten
        Assert.Empty(s.Repo.LastLockOrder);
    }

    // ================================================================ C: frequency

    [Fact] // C (the approved C1 example)
    public async Task FrequencyDailyToWeekly_OnThe29th_ReDatesTheSameOccurrenceToThe2ndOfOctober()
    {
        var s = await CreateAsync();

        await Update(s, Edit(s, frequency: "Weekly"));

        var open = Open(s);
        Assert.Equal(("MPM-0007", new DateOnly(2026, 10, 2)), (open.PmNo, open.ScheduledDate)); // anchor 25-Sep + 1 week
        Assert.Equal(7, s.Repo.PmSequence);                                                      // no new number
        Assert.Single(s.Repo.Pms, p => p.ChecklistId == s.Press.ChecklistId && p.Status == MachinePmStatus.Scheduled);
        Assert.Equal(new DateOnly(2026, 10, 2), s.Repo.StoredMachine(2).NextMaintenanceDate);
        Assert.Equal((new DateTime(2026, 9, 29, 5, 0, 0, DateTimeKind.Utc), (int?)1), (open.UpdatedAt!.Value, open.UpdatedBy));
    }

    [Fact] // C: the anchor never moves
    public async Task SuccessiveFrequencyChanges_AlwaysCountFromTheOriginalAnchor()
    {
        var s = await CreateAsync();

        await Update(s, Edit(s, frequency: "Monthly"));
        Assert.Equal(new DateOnly(2026, 10, 25), Open(s).ScheduledDate);

        s.Clock.UtcNow = new DateTime(2026, 10, 3, 5, 0, 0, DateTimeKind.Utc);
        await Update(s, Edit(s, frequency: "Weekly"));
        Assert.Equal(new DateOnly(2026, 10, 9), Open(s).ScheduledDate); // 25-Sep + 2 weeks (first on/after 3-Oct), not 3-Oct + 7
    }

    [Fact] // month-end through the engine
    public async Task FrequencyChangeToMonthly_OnA31stAnchor_KeepsTheCycleDay()
    {
        var s = await CreateAsync();
        s.Repo.Stored(s.Press.ChecklistId).StartDate = new DateOnly(2027, 1, 31); // the anchor
        s.Clock.UtcNow = new DateTime(2027, 3, 2, 5, 0, 0, DateTimeKind.Utc);

        await Update(s, Edit(s, frequency: "Monthly"));

        Assert.Equal(new DateOnly(2027, 3, 31), Open(s).ScheduledDate); // not 28-Mar
    }

    // ================================================================ D: machine

    [Fact] // D
    public async Task MachineChanged_MovesTheSameOccurrence_KeepsItsDate_AndRecalculatesBothMachines()
    {
        var s = await CreateAsync();
        s.Repo.StoredPm("MPM-0006").ScheduledDate = new DateOnly(2026, 10, 30); // make machine 1's other open occurrence LATER

        var dto = await Update(s, Edit(s, machineId: 1));

        var open = Open(s);
        Assert.Equal(("MPM-0007", 1, Anchor), (open.PmNo, open.MachineId, open.ScheduledDate)); // same number, same (overdue) date
        Assert.Equal(7, s.Repo.PmSequence);
        Assert.Null(s.Repo.StoredMachine(2).NextMaintenanceDate);                  // old machine: no open occurrence left
        Assert.Equal(Anchor, s.Repo.StoredMachine(1).NextMaintenanceDate);         // new machine: earliest open = MPM-0007
        Assert.Equal(new[] { 1, 2 }, s.Repo.LastLockOrder);                        // ascending id order
        Assert.Equal(("MAC-0001", 1), (dto.MachineCode, dto.MachineId));
    }

    [Fact]
    public async Task MachineAndFrequencyChangedTogether_MovesAndReDates()
    {
        var s = await CreateAsync();

        await Update(s, Edit(s, machineId: 1, frequency: "Weekly"));

        Assert.Equal(("MPM-0007", 1, new DateOnly(2026, 10, 2)), (Open(s).PmNo, Open(s).MachineId, Open(s).ScheduledDate));
        Assert.Equal(LegacyDue, s.Repo.StoredMachine(1).NextMaintenanceDate); // MPM-0006 (Feb) is still earlier
    }

    // ================================================================ E, F: applies-to

    [Fact] // E (approved C2)
    public async Task MachineToMold_ClearsTheMachine_KeepsTheOpenOccurrenceAsItIs_NoNewPm()
    {
        var s = await CreateAsync();
        var history = History(s);
        var before = (Open(s).PmNo, Open(s).MachineId, Open(s).ScheduledDate, string.Join(";", Open(s).ChecklistItems.Select(l => l.ItemLabel)));

        var dto = await Update(s, Edit(s, appliesTo: "Mold", machineId: null, items: new[] { "Cavities cleaned" }));

        Assert.Equal(("Mold", (int?)null), (dto.AppliesTo, dto.MachineId));
        Assert.Equal(before, (Open(s).PmNo, Open(s).MachineId, Open(s).ScheduledDate, string.Join(";", Open(s).ChecklistItems.Select(l => l.ItemLabel)))); // untouched, still completable
        Assert.Equal(7, s.Repo.PmSequence);
        Assert.Equal(Anchor, s.Repo.StoredMachine(2).NextMaintenanceDate); // it is still open on machine 2
        Assert.Equal(history, History(s));
        Assert.Contains(s.Audit.Entries.Single().Details, d => d is { FieldName: "machine", OldValue: "MAC-0002", NewValue: null });
    }

    [Fact] // F: Mold -> Machine with no occurrence at all
    public async Task MoldToMachine_WithNoOccurrence_CreatesTheFirstOccurrence_OnTheSelectedMachine()
    {
        var s = await CreateAsync(frequency: "Weekly", appliesTo: "Mold", machineId: null);
        Assert.Equal(6, s.Repo.Pms.Count); // a Mold checklist had no PM

        var dto = await Update(s, Edit(s, appliesTo: "Machine", machineId: 2));

        var created = Open(s);
        Assert.Equal(("MPM-0007", 2, new DateOnly(2026, 10, 2)), (created.PmNo, created.MachineId, created.ScheduledDate)); // anchor 25-Sep, Weekly, first on/after 29-Sep
        Assert.Null(created.MaintenanceTypeId);
        Assert.Equal(dto.Items.Select(i => i.ItemLabel), created.ChecklistItems.Select(l => l.ItemLabel));
        Assert.Equal(new DateOnly(2026, 10, 2), s.Repo.StoredMachine(2).NextMaintenanceDate);
        Assert.Equal(new[] { "ChecklistUpdated", "MachinePmScheduled" }, s.Audit.Entries.Select(e => e.Action));
        Assert.Contains(s.Audit.Entries[0].Details, d => d is { FieldName: "machine", OldValue: null, NewValue: "MAC-0002" });
    }

    [Fact] // F: Mold -> Machine reuses the occurrence kept by an earlier Machine -> Mold
    public async Task MachineToMoldToMachine_ReusesTheKeptOccurrence_MovedReDatedRefreshed_NoSecondPm()
    {
        var s = await CreateAsync();
        await Update(s, Edit(s, appliesTo: "Mold", machineId: null));

        await Update(s, Edit(s, appliesTo: "Machine", machineId: 1, items: new[] { "New step" }));

        var open = Assert.Single(s.Repo.Pms, p => p.ChecklistId == s.Press.ChecklistId && p.Status == MachinePmStatus.Scheduled);
        Assert.Equal(("MPM-0007", 1, Edit29), (open.PmNo, open.MachineId, open.ScheduledDate)); // Daily: first on/after 29-Sep
        Assert.Equal(new[] { "New step" }, open.ChecklistItems.Select(l => l.ItemLabel));
        Assert.Equal(7, s.Repo.PmSequence);
        Assert.Null(s.Repo.StoredMachine(2).NextMaintenanceDate);
    }

    [Fact] // F + I
    public async Task MoldToMachine_RequiresAnActiveMachine_NothingWritten()
    {
        var s = await CreateAsync(appliesTo: "Mold", machineId: null);
        var before = Everything(s);

        await Assert.ThrowsAsync<ValidationException>(() => Update(s, Edit(s, appliesTo: "Machine", machineId: null)));
        await Assert.ThrowsAsync<ValidationException>(() => Update(s, Edit(s, appliesTo: "Machine", machineId: 3)));

        Assert.Equal(before, Everything(s));
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ G: deactivation

    [Fact] // G
    public async Task Deactivate_KeepsTheOpenOccurrence_AndTheMachineDate()
    {
        var s = await CreateAsync();
        var before = (Open(s).PmNo, Open(s).MachineId, Open(s).ScheduledDate);

        var dto = await s.Service.DeactivateAsync(s.Press.ChecklistId, 1, null, CancellationToken.None);

        Assert.False(dto.IsActive);
        Assert.Equal(before, (Open(s).PmNo, Open(s).MachineId, Open(s).ScheduledDate));
        Assert.Equal(Anchor, s.Repo.StoredMachine(2).NextMaintenanceDate);
        Assert.Equal(7, s.Repo.Pms.Count(p => p.PmNo.StartsWith("MPM-000", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task EditingAnInactiveChecklist_NeverTouchesItsOpenOccurrence()
    {
        var s = await CreateAsync();
        await s.Service.DeactivateAsync(s.Press.ChecklistId, 1, null, CancellationToken.None);
        var before = string.Join(";", Open(s).ChecklistItems.Select(l => l.ItemLabel));

        await Update(s, Edit(s, items: new[] { "Changed after deactivation" }, frequency: "Weekly"));

        Assert.Equal(before, string.Join(";", Open(s).ChecklistItems.Select(l => l.ItemLabel)));
        Assert.Equal(Anchor, Open(s).ScheduledDate);
    }

    // ================================================================ H - L: refusals write nothing

    [Fact] // H, I, J
    public async Task InvalidFrequency_InactiveOrUnknownMachine_AreRefused_NothingWritten()
    {
        var s = await CreateAsync();
        var before = Everything(s);

        await Assert.ThrowsAsync<ValidationException>(() => Update(s, Edit(s, frequency: "Hourly")));  // H 400
        await Assert.ThrowsAsync<ValidationException>(() => Update(s, Edit(s, machineId: 3)));         // I 400
        await Assert.ThrowsAsync<NotFoundException>(() => Update(s, Edit(s, machineId: 99)));          // J 404

        Assert.Equal(0, s.Repo.UpdateCalls);
        Assert.Equal(before, Everything(s));
        Assert.Empty(s.Audit.Entries);
    }

    [Fact] // K
    public async Task StaleRowVersion_Is409_NothingWritten()
    {
        var s = await CreateAsync();
        var stale = Edit(s, frequency: "Weekly", machineId: 1, items: new[] { "x" });
        s.Repo.SimulateConcurrentModification(s.Press.ChecklistId);
        var before = Everything(s);

        await Assert.ThrowsAsync<ConflictException>(() => Update(s, stale));

        Assert.Equal(before, Everything(s));
        Assert.Empty(s.Audit.Entries);
    }

    [Fact] // L
    public async Task ConcurrentUpdate_TheFirstWins_TheSecondIs409_AndChangesNothing()
    {
        var s = await CreateAsync();
        var first = Edit(s, frequency: "Weekly");
        var second = Edit(s, machineId: 1); // read the same row version

        await Update(s, first);
        var afterFirst = Everything(s);
        await Assert.ThrowsAsync<ConflictException>(() => Update(s, second));

        Assert.Equal(afterFirst, Everything(s));
        Assert.Equal((2, new DateOnly(2026, 10, 2)), (Open(s).MachineId, Open(s).ScheduledDate));
    }

    [Theory] // transaction: a failure after the checklist rows rolls back everything
    [InlineData(true)]
    [InlineData(false)]
    public async Task AFailureInsideTheTransaction_RollsBackChecklistItemsOccurrenceAndMachines(bool atOccurrence)
    {
        var s = await CreateAsync();
        var before = Everything(s);
        if (atOccurrence) s.Repo.FailAtOccurrenceSync = true; else s.Repo.FailAtMachineUpdate = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => Update(s, Edit(s, frequency: "Weekly", machineId: 1, items: new[] { "x", "y" })));

        Assert.Equal(before, Everything(s));
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ M: no open occurrence

    [Fact] // M
    public async Task WithNoOpenOccurrence_AnItemOrMachineEdit_CreatesNoPm()
    {
        var s = await CreateAsync();
        Open(s).Status = MachinePmStatus.Completed; // e.g. completed by Function 5 without a successor
        var pmCount = s.Repo.Pms.Count;

        await Update(s, Edit(s, items: new[] { "Only step" }, machineId: 1));

        Assert.Equal((pmCount, 7), (s.Repo.Pms.Count, s.Repo.PmSequence));
    }

    // ================================================================ audit

    [Fact]
    public async Task TheUpdateAudit_DescribesTheOccurrenceAndMachineChanges()
    {
        var s = await CreateAsync();

        await Update(s, Edit(s, frequency: "Weekly", machineId: 1, items: new[] { "Oil level checked", "Belts checked" }));

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("ChecklistUpdated", entry.Action);
        Assert.StartsWith("Sakthi updated checklist 'Press Daily'", entry.Description);
        Assert.Contains("Changed: frequency, machine, items.", entry.Description);
        Assert.Contains("open PM MPM-0007 moved from MAC-0002 to MAC-0001; open PM MPM-0007 re-dated from 2026-09-25 to 2026-10-02; open PM MPM-0007 checklist refreshed.", entry.Description);
        Assert.Contains(entry.Details, d => d is { FieldName: "frequency", OldValue: "Daily", NewValue: "Weekly" });
        Assert.Contains(entry.Details, d => d is { FieldName: "machine", OldValue: "MAC-0002", NewValue: "MAC-0001" });
        Assert.Contains(entry.Details, d => d is { FieldName: "open_pm_scheduled_date (MPM-0007)", OldValue: "2026-09-25", NewValue: "2026-10-02" });
        Assert.Contains(entry.Details, d => d is { FieldName: "open_pm_machine (MPM-0007)", OldValue: "MAC-0002", NewValue: "MAC-0001" });
        Assert.Contains(entry.Details, d => d is { FieldName: "open_pm_snapshot (MPM-0007)", OldValue: "3 items", NewValue: "2 items" });
        Assert.Contains(entry.Details, d => d is { FieldName: "machine_next_maintenance_date (MAC-0002)", OldValue: "2026-09-25", NewValue: null });
        Assert.All(entry.Details, d => Assert.True(d.FieldName.Length <= 100)); // audit_log_detail.field_name NVARCHAR(100)
    }
}
