using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Domain.Rules;
using GlobalRubber.MMM.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The real MachinePmService (recurring occurrences, Function 5) against in-memory fakes. Acting user 1 "Sakthi".
/// Machines: 1 MAC-0001 (Running), 2 MAC-0002 (Breakdown) - active; 3 inactive.
/// </summary>
public class MachinePmServiceTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private sealed record Sut(MachinePmService Service, InMemoryMachinePmRepository Repo, MachinePmScenario Data, RecordingAuditLog Audit, SettableClock Clock, InMemoryUserRepository Users);

    private static Sut Create(MachinePmScenario data, DateOnly today, IAuditLogService? auditOverride = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var repo = new InMemoryMachinePmRepository(data);
        var audit = new RecordingAuditLog();
        var clock = new SettableClock(today);
        return new Sut(new MachinePmService(repo, users, clock, auditOverride ?? audit, NullLogger<MachinePmService>.Instance), repo, data, audit, clock, users);
    }

    private static CompleteMachinePmRequest Done(MachinePm pm, string? by = "Ravi", string? remarks = null, params (int Line, bool Checked)[] ticks) =>
        new() { MaintenanceBy = by, Remarks = remarks, RowVersion = Convert.ToBase64String(pm.RowVersion), Results = ticks.Select(t => new MachinePmChecklistResultRequest { MachinePmChecklistId = t.Line, IsChecked = t.Checked }).ToList() };

    private static Task<MachinePmDto> Complete(Sut s, MachinePm pm, CompleteMachinePmRequest? request = null) =>
        s.Service.CompleteAsync(pm.MachinePmId, request ?? Done(pm), 1, "10.0.0.5", CancellationToken.None);

    private static MachinePm Successor(Sut s, MaintenanceChecklist c) => Assert.Single(s.Data.Pms, p => p.ChecklistId == c.ChecklistId && p.Status == MachinePmStatus.Scheduled);

    // ================================================================ completing the PM

    [Fact]
    public async Task Complete_SetsCompletedToday_MaintenanceByTrimmed_TicksAndRemarks_KeepsDueDateMachineChecklistAndNumber()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25));
        var pm = data.Open(c, D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 27));

        var dto = await Complete(s, pm, Done(pm, by: "  Ravi Kumar  ", remarks: "  replaced filter ", (pm.ChecklistItems[1].MachinePmChecklistId, true)));

        Assert.Equal((MachinePmStatus.Completed, D(2026, 9, 27), "Ravi Kumar", "replaced filter"), (dto.Status, dto.CompletedDate!.Value, dto.MaintenanceBy, dto.Remarks));
        Assert.Equal((pm.PmNo, D(2026, 9, 25), 1, (int?)c.ChecklistId), (dto.PmNo, dto.ScheduledDate, dto.MachineId, dto.ChecklistId));
        Assert.Equal(new[] { false, true }, dto.ChecklistItems.Select(i => i.IsChecked)); // unchecked items allowed (Q-17)
        Assert.NotEqual("AQ==", dto.RowVersion); // the version it was read with
        var stored = s.Repo.Stored(pm.MachinePmId);
        Assert.Equal(("Ravi Kumar", D(2026, 9, 27)), (stored.MaintenanceBy, stored.CompletedDate!.Value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Complete_WithoutMaintenanceBy_Is400_NothingWritten(string? by)
    {
        var data = new MachinePmScenario();
        var pm = data.Open(data.Checklist("Daily", D(2026, 9, 25)), D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 25));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Complete(s, pm, Done(pm, by: by)));

        Assert.Contains("MaintenanceBy is required.", ex.Errors);
        AssertUntouched(s, pm);
    }

    [Fact]
    public async Task MaintenanceBy_IsAtMost100Characters()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25));
        var pm = data.Open(c, D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 25));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Complete(s, pm, Done(pm, by: new string('x', 101))));
        Assert.Contains("MaintenanceBy must be at most 100 characters.", ex.Errors);

        var dto = await Complete(s, pm, Done(pm, by: new string('x', 100)));
        Assert.Equal(100, dto.MaintenanceBy!.Length);
    }

    [Fact]
    public async Task Complete_RejectsResultsOfAnotherPm_DuplicateResults_BadRowVersionAndLongRemarks_NothingWritten()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25));
        var pm = data.Open(c, D(2026, 9, 25));
        var other = data.Completed(data.Checklist("Weekly", D(2026, 9, 1), machineId: 2), D(2026, 9, 1));
        var s = Create(data, D(2026, 9, 25));
        var line = pm.ChecklistItems[0].MachinePmChecklistId;

        var ex = await Assert.ThrowsAsync<ValidationException>(() => Complete(s, pm, new CompleteMachinePmRequest
        {
            MaintenanceBy = "Ravi", RowVersion = "", Remarks = new string('r', 1001),
            Results = new[] { new MachinePmChecklistResultRequest { MachinePmChecklistId = other.ChecklistItems[0].MachinePmChecklistId }, new MachinePmChecklistResultRequest { MachinePmChecklistId = line }, new MachinePmChecklistResultRequest { MachinePmChecklistId = line } },
        }));

        Assert.Equal(new[]
        {
            "RowVersion is required.", "Remarks must be at most 1000 characters.",
            "A checklist result does not belong to this maintenance record.", "A checklist item is listed more than once.",
        }, ex.Errors);
        var bad = await Assert.ThrowsAsync<ValidationException>(() => Complete(s, pm, new CompleteMachinePmRequest { MaintenanceBy = "Ravi", RowVersion = "not base64 !!" }));
        Assert.Contains("RowVersion is not valid.", bad.Errors);
        AssertUntouched(s, pm);
    }

    [Fact]
    public async Task Complete_UnknownIs404_AlreadyCompletedIs409_WithoutWriting()
    {
        var data = new MachinePmScenario();
        var done = data.Completed(data.Checklist("Daily", D(2026, 9, 20)), D(2026, 9, 20));
        var s = Create(data, D(2026, 9, 25));

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CompleteAsync(999, Done(done), 1, null, CancellationToken.None));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => Complete(s, done));

        Assert.Equal("The maintenance record is already completed.", ex.Message);
        Assert.Equal(0, s.Repo.CompleteCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task StaleRowVersion_Is409_AndNothingIsWritten_NoSuccessor_NoMachineChange()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25));
        var pm = data.Open(c, D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 25));
        var request = Done(pm);
        s.Repo.SimulateConcurrentModification(pm.MachinePmId);

        await Assert.ThrowsAsync<ConflictException>(() => Complete(s, pm, request));

        Assert.Equal(MachinePmStatus.Scheduled, s.Repo.Stored(pm.MachinePmId).Status);
        Assert.Single(s.Data.Pms);
        Assert.Equal(1, s.Data.PmSequence);
        Assert.Equal(D(2026, 2, 1), s.Repo.StoredMachine(1).LastMaintenanceDate);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task TwoCompletionsOfTheSamePm_TheSecondIs409_AndThereIsNoSecondSuccessor()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25));
        var pm = data.Open(c, D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 25));
        var first = Done(pm);
        var second = Done(pm, by: "Karthik"); // both read the same row version

        await Complete(s, pm, first);
        await Assert.ThrowsAsync<ConflictException>(() => Complete(s, pm, second));

        Assert.Equal(2, s.Data.Pms.Count); // the PM + ONE successor
        Assert.Equal("Ravi", s.Repo.Stored(pm.MachinePmId).MaintenanceBy);
    }

    [Fact]
    public async Task AFailureInsideTheTransaction_LeavesThePmUncompleted_NoSuccessor_MachineUntouched_NoAudit()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25));
        var pm = data.Open(c, D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 25));
        s.Repo.FailNextSave = true;

        await Assert.ThrowsAsync<DbUpdateException>(() => Complete(s, pm));

        AssertUntouched(s, pm);
        Assert.Equal(1, s.Data.PmSequence);
    }

    // ================================================================ the successor (approved cycle rules)

    [Theory]
    [InlineData("Daily", "2026-09-25", "2026-09-25", "2026-09-27", "2026-09-28")]   // late: skip 26, 27
    [InlineData("Daily", "2026-09-20", "2026-09-20", "2026-09-27", "2026-09-28")]   // many missed cycles: no backlog
    [InlineData("Daily", "2026-09-25", "2026-09-25", "2026-09-25", "2026-09-26")]   // on time
    [InlineData("Weekly", "2026-09-25", "2026-09-25", "2026-09-27", "2026-10-02")]  // keeps the cycle weekday
    [InlineData("Monthly", "2026-08-10", "2026-09-10", "2026-09-15", "2026-10-10")] // keeps the cycle day
    [InlineData("Yearly", "2025-09-25", "2026-09-25", "2026-09-30", "2027-09-25")]
    [InlineData("Monthly", "2026-09-25", "2026-10-25", "2026-10-01", "2026-11-25")] // EARLY completion: never the same occurrence again
    [InlineData("Monthly", "2027-01-31", "2027-02-28", "2027-02-28", "2027-03-31")] // month end: back to the 31st
    [InlineData("Yearly", "2028-02-29", "2029-02-28", "2029-03-02", "2030-02-28")]  // leap-day anchor
    [InlineData("Yearly", "2028-02-29", "2031-02-28", "2031-03-01", "2032-02-29")]  // ... returns to the 29th in a leap year
    [InlineData("Daily", "2026-09-25", "2026-09-25", "2026-09-26", "2026-09-27")]   // final-flow spec 1: completed a day late
    [InlineData("Daily", "2026-09-25", "2026-09-25", "2026-09-28", "2026-09-29")]   // spec 6: MPM due 25, completed 28
    [InlineData("Daily", "2026-09-25", "2026-09-25", "2026-10-05", "2026-10-06")]   // spec 12: completed 05-Oct, no backlog
    [InlineData("Weekly", "2026-09-25", "2026-09-25", "2026-10-02", "2026-10-09")]  // spec 7: missed, completed ON a cycle date
    [InlineData("Monthly", "2026-01-31", "2026-02-28", "2026-03-15", "2026-03-31")] // spec 8: missed 28-Feb, completed 15-Mar -> 31-Mar
    [InlineData("Yearly", "2028-02-29", "2029-02-28", "2029-06-01", "2030-02-28")]  // spec 9: missed leap-cycle yearly PM
    public async Task TheSuccessor_IsTheFirstCycleDateAfterDueAndCompletion(string frequency, string anchor, string due, string completed, string expected)
    {
        var data = new MachinePmScenario();
        var c = data.Checklist(frequency, DateOnly.Parse(anchor));
        var pm = data.Open(c, DateOnly.Parse(due));
        var s = Create(data, DateOnly.Parse(completed));

        await Complete(s, pm);

        var next = Successor(s, c);
        Assert.Equal(DateOnly.Parse(expected), next.ScheduledDate);
        Assert.Equal(RecurrenceRules.NextDueAfterCompletion(DateOnly.Parse(anchor), frequency, DateOnly.Parse(due), DateOnly.Parse(completed)), next.ScheduledDate);
    }

    [Theory] // the task's examples: the cycle stays on the start date whatever the completion date
    [InlineData("Daily", "2026-09-26", "2026-09-27", "2026-09-28")]    // PM1 25-Sep done late 26-Sep -> 27-Sep -> 28-Sep
    [InlineData("Weekly", "2026-09-27", "2026-10-02", "2026-10-09")]   // PM1 25-Sep done 27-Sep -> 02-Oct -> 09-Oct
    [InlineData("Monthly", "2026-09-30", "2026-10-25", "2026-11-25")]  // PM1 25-Sep done 30-Sep -> 25-Oct -> 25-Nov
    [InlineData("Yearly", "2026-09-30", "2027-09-25", "2028-09-25")]
    public async Task ACompletionChain_StaysOnTheStartDateCycle(string frequency, string firstDone, string second, string third)
    {
        var data = new MachinePmScenario();
        var c = data.Checklist(frequency, D(2026, 9, 25));
        var pm = data.Open(c, D(2026, 9, 25));
        var s = Create(data, DateOnly.Parse(firstDone));

        await Complete(s, pm);
        var pm2 = Successor(s, c);
        Assert.Equal(DateOnly.Parse(second), pm2.ScheduledDate);

        s.Clock.SetToday(pm2.ScheduledDate); // completed on its due date
        await Complete(s, pm2);
        Assert.Equal(DateOnly.Parse(third), Successor(s, c).ScheduledDate);
        Assert.Equal(3, s.Data.Pms.Count(p => p.ChecklistId == c.ChecklistId));
    }

    [Fact]
    public async Task ALegacyChecklistWithoutAStartDate_UsesItsCreationDateInIstAsTheAnchor()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Weekly", D(2026, 9, 25));
        c.StartDate = null;
        c.CreatedAt = new DateTime(2026, 9, 21, 4, 0, 0, DateTimeKind.Utc); // Monday 21-Sep IST
        var pm = data.Open(c, D(2026, 9, 28));
        var s = Create(data, D(2026, 9, 28));

        await Complete(s, pm);

        Assert.Equal(D(2026, 10, 5), Successor(s, c).ScheduledDate);
    }

    [Fact]
    public async Task TheSuccessor_IsExactlyOneNewOccurrence_WithTheApprovedFields_AndASnapshotOfTheCURRENTItems()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25), labels: new[] { "Old step A", "Old step B" });
        var pm = data.Open(c, D(2026, 9, 25));
        // the checklist changed after the PM was created (Function 4 would have refreshed an OPEN snapshot; here only the master)
        c.Items = new List<MaintenanceChecklistItem> { new() { ChecklistItemId = 777, ChecklistId = c.ChecklistId, SortOrder = 1, ItemLabel = "New step" } };
        var s = Create(data, D(2026, 9, 25));

        await Complete(s, pm, Done(pm, ticks: (pm.ChecklistItems[0].MachinePmChecklistId, true)));

        var next = Successor(s, c);
        Assert.Equal(("MPM-0002", 1, (int?)c.ChecklistId, MachinePmStatus.Scheduled), (next.PmNo, next.MachineId, next.ChecklistId, next.Status));
        Assert.Null(next.MaintenanceTypeId);
        Assert.Null(next.EngineerId);
        Assert.Null(next.MaintenanceBy);
        Assert.Null(next.CompletedDate);
        Assert.Equal((777, "New step", false), (next.ChecklistItems.Single().ChecklistItemId!.Value, next.ChecklistItems.Single().ItemLabel, next.ChecklistItems.Single().IsChecked));
        // the completed PM keeps ITS snapshot (history)
        Assert.Equal(new[] { ("Old step A", true), ("Old step B", false) }, s.Repo.Stored(pm.MachinePmId).ChecklistItems.Select(l => (l.ItemLabel, l.IsChecked)));
        Assert.Equal(2, s.Data.PmSequence);
    }

    [Fact]
    public async Task ACompletedSnapshot_NeverChangesAfterwards()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25));
        var pm = data.Open(c, D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 25));
        await Complete(s, pm, Done(pm, ticks: (pm.ChecklistItems[1].MachinePmChecklistId, true)));
        var history = string.Join("|", s.Repo.Stored(pm.MachinePmId).ChecklistItems.Select(l => $"{l.MachinePmChecklistId}:{l.ItemLabel}:{l.IsChecked}"));

        s.Clock.SetToday(D(2026, 9, 26));
        await Complete(s, Successor(s, c)); // the next occurrence

        Assert.Equal(history, string.Join("|", s.Repo.Stored(pm.MachinePmId).ChecklistItems.Select(l => $"{l.MachinePmChecklistId}:{l.ItemLabel}:{l.IsChecked}")));
        Assert.Equal(3, s.Data.Pms.Count); // 25 completed, 26 completed, 27 scheduled
        Assert.Equal(D(2026, 9, 27), Successor(s, c).ScheduledDate);
    }

    [Fact]
    public async Task AnInactiveChecklist_GetsNoSuccessor()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25), active: false);
        var pm = data.Open(c, D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 25));

        var dto = await Complete(s, pm);

        Assert.Equal(MachinePmStatus.Completed, dto.Status);
        Assert.Single(s.Data.Pms);
        Assert.Equal(1, s.Data.PmSequence);
        Assert.Null(s.Repo.StoredMachine(1).NextMaintenanceDate); // no open occurrence left on the machine
    }

    [Fact]
    public async Task AChecklistChangedToMold_GetsNoMachineSuccessor()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25));
        var pm = data.Open(c, D(2026, 9, 25)); // kept open by Machine -> Mold (C2)
        c.AppliesTo = "Mold";
        c.MachineId = null;
        var s = Create(data, D(2026, 9, 25));

        await Complete(s, pm);

        Assert.Single(s.Data.Pms);
    }

    [Fact]
    public async Task APmWithoutAChecklist_GetsNoSuccessor()
    {
        var data = new MachinePmScenario();
        var pm = data.Open(null, D(2026, 9, 25), machineId: 1);
        var s = Create(data, D(2026, 9, 25));

        await Complete(s, pm);

        Assert.Single(s.Data.Pms);
    }

    // ================================================================ machine dates

    [Fact]
    public async Task MachineDates_LastIsTheCompletionDate_NextIsTheEarliestOpenOccurrence_MaintenanceBecomesRunning()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Weekly", D(2026, 9, 25));
        var pm = data.Open(c, D(2026, 9, 25));
        data.Open(data.Checklist("Monthly", D(2026, 9, 29)), D(2026, 9, 29)); // another open occurrence on machine 1, earlier than the successor
        data.Machines.Single(m => m.MachineId == 1).OperationalStatus = MachineOperationalStatus.Maintenance;
        var s = Create(data, D(2026, 9, 27));

        await Complete(s, pm);

        var machine = s.Repo.StoredMachine(1);
        Assert.Equal(D(2026, 9, 27), machine.LastMaintenanceDate);
        Assert.Equal(D(2026, 9, 29), machine.NextMaintenanceDate); // not the successor (02-Oct): the earliest open
        Assert.Equal(MachineOperationalStatus.Running, machine.OperationalStatus);
        Assert.Equal((s.Clock.UtcNow, (int?)1), (machine.UpdatedAt!.Value, machine.UpdatedBy));
    }

    [Fact]
    public async Task MachineDates_NextIsTheSuccessor_WhenItIsTheOnlyOpenOccurrence_BreakdownStaysBreakdown()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25), machineId: 2);
        var pm = data.Open(c, D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 25));

        await Complete(s, pm);

        Assert.Equal((D(2026, 9, 26), MachineOperationalStatus.Breakdown), (s.Repo.StoredMachine(2).NextMaintenanceDate!.Value, s.Repo.StoredMachine(2).OperationalStatus));
    }

    [Fact]
    public async Task ThePmsMachine_AndTheChecklistsMachine_AreLockedInAscendingOrder()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25), machineId: 2);
        var pm = data.Open(c, D(2026, 9, 25), machineId: 1);
        var s = Create(data, D(2026, 9, 25));

        await Complete(s, pm);

        Assert.Equal(new[] { 1, 2 }, s.Repo.LastLockOrder);
        Assert.Equal(2, Successor(s, c).MachineId); // the successor goes on the checklist's machine
    }

    // ================================================================ audit

    [Fact]
    public async Task Audit_MachinePmCompleted_ThenMachinePmScheduledForTheSuccessor()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25));
        var pm = data.Open(c, D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 27));

        await Complete(s, pm, Done(pm, by: "Ravi", remarks: "ok", ticks: (pm.ChecklistItems[0].MachinePmChecklistId, true)));

        Assert.Equal(new[] { "MachinePmCompleted", "MachinePmScheduled" }, s.Audit.Entries.Select(e => e.Action));
        var done = s.Audit.Entries[0];
        Assert.Equal(("Machine Preventive Maintenance", "MachinePm", pm.PmNo, "10.0.0.5", "Sakthi"), (done.Module, done.EntityName, done.RecordRef, done.IpAddress, done.UserName));
        Assert.Equal($"Sakthi completed machine PM {pm.PmNo} for machine MAC-0001 (due 2026-09-25) on 2026-09-27, performed by Ravi (1 of 2 checklist items checked). Next occurrence MPM-0002 due 2026-09-28.", done.Description);
        Assert.Contains(done.Details, d => d is { FieldName: "status", OldValue: "Scheduled", NewValue: "Completed" });
        Assert.Contains(done.Details, d => d is { FieldName: "completed_date", NewValue: "2026-09-27" });
        Assert.Contains(done.Details, d => d is { FieldName: "maintenance_by", NewValue: "Ravi" });
        Assert.Contains(done.Details, d => d is { FieldName: "remarks", NewValue: "ok" });
        Assert.Contains(done.Details, d => d is { FieldName: "successor", NewValue: "MPM-0002 due 2026-09-28" });
        Assert.Contains(done.Details, d => d is { FieldName: "machine_last_maintenance_date (MAC-0001)", OldValue: "2026-02-01", NewValue: "2026-09-27" });
        Assert.Contains(done.Details, d => d is { FieldName: "machine_next_maintenance_date (MAC-0001)", NewValue: "2026-09-28" });
        Assert.Equal("MPM-0002", s.Audit.Entries[1].RecordRef);
        Assert.All(s.Audit.Entries, e => Assert.True(e.Action.Length <= 30));
    }

    [Fact]
    public async Task Audit_WithoutASuccessor_SaysSo()
    {
        var data = new MachinePmScenario();
        var pm = data.Open(data.Checklist("Daily", D(2026, 9, 25), active: false), D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 25));

        await Complete(s, pm);

        Assert.EndsWith("No next occurrence.", Assert.Single(s.Audit.Entries).Description);
    }

    [Fact]
    public async Task WhenTheAuditWriteFails_TheCompletionAndSuccessorStillStand()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25));
        var pm = data.Open(c, D(2026, 9, 25));
        var failing = new AuditLogService(new ThrowingAuditRepository(), new FixedClock(), NullLogger<AuditLogService>.Instance);
        var s = Create(data, D(2026, 9, 25), failing);

        var dto = await Complete(s, pm);

        Assert.Equal(MachinePmStatus.Completed, dto.Status);
        Assert.Equal(D(2026, 9, 26), Successor(s, c).ScheduledDate);
    }

    [Fact]
    public async Task WhenTheActingUsersNameCannotBeResolved_TheCompletionStillSucceeds()
    {
        var data = new MachinePmScenario();
        var pm = data.Open(data.Checklist("Daily", D(2026, 9, 25), active: false), D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 25));
        s.Users.ThrowOnGetByIdCall = 1;

        await Complete(s, pm);

        Assert.Equal("Unknown", Assert.Single(s.Audit.Entries).UserName);
    }

    // ================================================================ reads

    // ================================================================ tabs = checklist FREQUENCY, DUE work only (scheduled <= today IST)

    /// <summary>
    /// Today 2026-09-25. Per frequency: one open due TODAY, one open OVERDUE, one open FUTURE (not yet due) and one
    /// completed. Daily on machine 1, the rest on machine 2.
    /// </summary>
    private static Sut TabScenario()
    {
        var data = new MachinePmScenario();
        foreach (var (freq, machine) in new[] { ("Daily", 1), ("Weekly", 2), ("Monthly", 2), ("Yearly", 2) })
        {
            data.Open(data.Checklist(freq, D(2026, 9, 1), machineId: machine), D(2026, 9, 25));        // due today
            data.Open(data.Checklist(freq, D(2026, 9, 1), machineId: machine), D(2026, 9, 20));        // overdue
            data.Open(data.Checklist(freq, D(2026, 9, 1), machineId: machine), D(2026, 9, 26));        // not due yet
            data.Completed(data.Checklist(freq, D(2026, 9, 1), machineId: machine), D(2026, 9, 24));
        }

        return Create(data, D(2026, 9, 25));
    }

    private static async Task<List<MachinePmDto>> Tab(Sut s, string bucket, int? machineId = null, string? search = null) =>
        (await s.Service.GetAllAsync(new MachinePmListQuery { Bucket = bucket, MachineId = machineId, Search = search, PageSize = 100 }, CancellationToken.None)).Items.ToList();

    private static async Task<MachinePmBucketCountsDto> Counts(Sut s, int? machineId = null, string? search = null) =>
        await s.Service.GetBucketCountsAsync(new MachinePmListQuery { MachineId = machineId, Search = search }, CancellationToken.None);

    [Theory]
    [InlineData("daily", "Daily")]
    [InlineData("weekly", "Weekly")]
    [InlineData("monthly", "Monthly")]
    [InlineData("yearly", "Yearly")]
    public async Task AFrequencyTab_HoldsTheDueOpenPmsOfThatFrequency_OverdueIncluded_FutureAndCompletedExcluded(string bucket, string frequency)
    {
        var s = TabScenario();

        var items = await Tab(s, bucket);

        Assert.All(items, i => Assert.Equal((frequency, "Scheduled"), (i.Frequency, i.Status)));
        Assert.Equal(new[] { (D(2026, 9, 20), true), (D(2026, 9, 25), false) }, items.Select(i => (i.ScheduledDate, i.IsOverdue))); // overdue first; 26-Sep hidden
    }

    [Fact]
    public async Task TheCompletedTab_HoldsCompletedPmsOfEveryFrequency_KeepingTheirFrequency()
    {
        var s = TabScenario();

        var items = await Tab(s, "completed");

        Assert.Equal(new[] { "Daily", "Monthly", "Weekly", "Yearly" }, items.Select(i => i.Frequency).OrderBy(f => f));
        Assert.All(items, i => Assert.Equal(("Completed", false), (i.Status, i.IsOverdue)));
    }

    [Fact]
    public async Task Counts_CountOnlyDueWork_AndFollowTheMachineAndSearchFilters()
    {
        var s = TabScenario();

        var all = await Counts(s);
        Assert.Equal((2, 2, 2, 2, 4), (all.Daily, all.Weekly, all.Monthly, all.Yearly, all.Completed)); // the four 26-Sep PMs not counted
        var machine2 = await Counts(s, machineId: 2);
        Assert.Equal((0, 2, 2, 2, 3), (machine2.Daily, machine2.Weekly, machine2.Monthly, machine2.Yearly, machine2.Completed));
        var one = await Counts(s, search: "mpm-0002"); // the overdue Daily one
        Assert.Equal((1, 0, 0, 0, 0), (one.Daily, one.Weekly, one.Monthly, one.Yearly, one.Completed));
    }

    [Fact]
    public async Task MachineFilterAndSearch_WorkWithinTheSelectedTab()
    {
        var s = TabScenario();

        Assert.Empty(await Tab(s, "daily", machineId: 2));
        Assert.Equal(2, (await Tab(s, "daily", machineId: 1)).Count);
        Assert.Equal(new[] { "MPM-0006" }, (await Tab(s, "weekly", search: "mpm-0006")).Select(i => i.PmNo)); // the overdue Weekly one
        Assert.Empty(await Tab(s, "weekly", search: "mpm-0007")); // the future Weekly one - not due yet
        Assert.Empty(await Tab(s, "monthly", search: "mpm-0006"));
    }

    [Fact]
    public async Task WithoutATab_EveryPmIsListed_FutureOnesIncluded()
    {
        var s = TabScenario();

        var all = (await s.Service.GetAllAsync(new MachinePmListQuery { PageSize = 100 }, CancellationToken.None)).Items;

        Assert.Equal(16, all.Count);
        Assert.Equal(4, all.Count(i => i.ScheduledDate == D(2026, 9, 26)));
    }

    [Fact]
    public async Task Tabs_PageTenAtATime_OnTheServer()
    {
        var data = new MachinePmScenario();
        for (var i = 0; i < 12; i++) data.Open(data.Checklist("Daily", D(2026, 9, 1)), D(2026, 9, 25).AddDays(-i)); // due today or overdue
        data.Open(data.Checklist("Daily", D(2026, 9, 1)), D(2026, 9, 27));                                        // not due yet
        var s = Create(data, D(2026, 9, 25));

        var page1 = await s.Service.GetAllAsync(new MachinePmListQuery { Bucket = "daily", PageNumber = 1, PageSize = 10 }, CancellationToken.None);
        var page2 = await s.Service.GetAllAsync(new MachinePmListQuery { Bucket = "daily", PageNumber = 2, PageSize = 10 }, CancellationToken.None);

        Assert.Equal((12, 2, 10, 2), (page1.TotalCount, page1.TotalPages, page1.Items.Count, page2.Items.Count));
        Assert.Empty(page1.Items.Select(i => i.PmNo).Intersect(page2.Items.Select(i => i.PmNo)));
        Assert.Equal(11, page1.Items.Concat(page2.Items).Count(i => i.IsOverdue));
    }

    [Theory]
    [InlineData("today")]
    [InlineData("week")]
    [InlineData("overdue")]
    [InlineData("upcoming")]
    [InlineData("hourly")]
    public async Task TheOldCalendarTabs_AndUnknownTabs_Are400(string bucket)
    {
        var s = TabScenario();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.GetAllAsync(new MachinePmListQuery { Bucket = bucket }, CancellationToken.None));

        Assert.Contains("Bucket must be one of: daily, weekly, monthly, yearly, completed.", ex.Errors);
    }

    // ---------------------------------------------------------------- the successor is hidden until its date (items 1-8, 13, 14)

    [Theory]
    [InlineData("Daily", "daily", "2026-09-26")]
    [InlineData("Weekly", "weekly", "2026-10-02")]
    [InlineData("Monthly", "monthly", "2026-10-25")]
    [InlineData("Yearly", "yearly", "2027-09-25")]
    public async Task CompletingTodaysPm_CreatesTheSuccessorAtOnce_ButItStaysOutOfTheTabUntilItsDate(string frequency, string bucket, string next)
    {
        var data = new MachinePmScenario();
        var c = data.Checklist(frequency, D(2026, 9, 25)); // start date = today
        var pm = data.Open(c, D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 25));
        var nextDue = DateOnly.Parse(next);

        await Complete(s, pm);

        // the successor exists (one open occurrence, the right cycle date) ...
        Assert.Equal(nextDue, Successor(s, c).ScheduledDate);
        // ... the completed PM moved to Completed, and the successor is NOT in the frequency tab yet
        Assert.Equal(new[] { pm.PmNo }, (await Tab(s, "completed")).Select(i => i.PmNo));
        Assert.Empty(await Tab(s, bucket));
        Assert.Equal(0, (await Counts(s)).GetType().GetProperty(frequency)!.GetValue(await Counts(s)));

        // the day before its date: still hidden
        s.Clock.SetToday(nextDue.AddDays(-1));
        Assert.Empty(await Tab(s, bucket));

        // on its date: visible, not overdue
        s.Clock.SetToday(nextDue);
        var shown = Assert.Single(await Tab(s, bucket));
        Assert.Equal((Successor(s, c).PmNo, nextDue, false), (shown.PmNo, shown.ScheduledDate, shown.IsOverdue));
        Assert.Equal(1, (await Counts(s)).GetType().GetProperty(frequency)!.GetValue(await Counts(s)));

        // and the day after, still there - as overdue, in the same tab
        s.Clock.SetToday(nextDue.AddDays(1));
        Assert.True(Assert.Single(await Tab(s, bucket)).IsOverdue);
    }

    [Theory] // 23:59:59 IST -> hidden; 00:00:00 IST -> due. 00:00 IST on 26-Sep = 18:30 UTC on 25-Sep.
    [InlineData("2026-09-25T18:29:59", false)]
    [InlineData("2026-09-25T18:30:00", true)]
    public async Task TheSuccessorBecomesDue_AtMidnightIst_NotUtc(string utcNow, bool visible)
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25));
        var pm = data.Open(c, D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 25));
        await Complete(s, pm); // successor due 26-Sep

        s.Clock.UtcNow = DateTime.SpecifyKind(DateTime.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);

        Assert.Equal(visible ? 1 : 0, (await Tab(s, "daily")).Count);
        Assert.Equal(visible ? 1 : 0, (await Counts(s)).Daily);
    }

    // ---------------------------------------------------------------- missed maintenance: one overdue PM, no backlog (items 9-11)

    [Fact]
    public async Task AMissedDailyPm_StaysTheOnlyOpenPm_OverdueInDaily_ThenOneSuccessor_FromTheStartDateCycle()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25)); // start date 25-Sep
        var pm = data.Open(c, D(2026, 9, 28));
        var s = Create(data, D(2026, 9, 28));

        foreach (var day in new[] { D(2026, 9, 29), D(2026, 9, 30) }) // not completed: still the same single open PM, overdue
        {
            s.Clock.SetToday(day);
            var shown = Assert.Single(await Tab(s, "daily"));
            Assert.Equal((pm.PmNo, true), (shown.PmNo, shown.IsOverdue));
            Assert.Single(s.Data.Pms, p => p.ChecklistId == c.ChecklistId);
        }

        await Complete(s, pm); // late, on 30-Sep

        Assert.Equal(2, s.Data.Pms.Count(p => p.ChecklistId == c.ChecklistId)); // the completed one + ONE successor
        Assert.Equal(D(2026, 10, 1), Successor(s, c).ScheduledDate);           // not 29/30-Sep
        Assert.Empty(await Tab(s, "daily"));                                   // due tomorrow - hidden today
    }

    [Fact]
    public async Task AMissedWeeklyPm_CompletedOn10Oct_GetsOneSuccessorOn16Oct_NoBacklog()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Weekly", D(2026, 9, 25));
        var pm = data.Open(c, D(2026, 10, 2));
        var s = Create(data, D(2026, 10, 10));
        Assert.True(Assert.Single(await Tab(s, "weekly")).IsOverdue);

        await Complete(s, pm);

        Assert.Equal(D(2026, 10, 16), Successor(s, c).ScheduledDate);
        Assert.Equal(2, s.Data.Pms.Count(p => p.ChecklistId == c.ChecklistId)); // no 09-Oct occurrence
        Assert.Empty(await Tab(s, "weekly"));
        s.Clock.SetToday(D(2026, 10, 16));
        Assert.Single(await Tab(s, "weekly"));
    }

    // The final-flow examples, end to end: a missed PM stays the SAME single open PM (overdue, in its tab) every day; the
    // late completion creates ONE successor from the Start Date cycle, hidden until its date, then visible.
    [Theory]
    [InlineData("Daily", "daily", "2026-09-25", "2026-09-25", "2026-09-26,2026-09-27,2026-09-28", "2026-09-28", "2026-09-29")]   // spec 6
    [InlineData("Weekly", "weekly", "2026-09-25", "2026-09-25", "2026-09-26,2026-09-30,2026-10-02", "2026-10-02", "2026-10-09")] // spec 7
    [InlineData("Monthly", "monthly", "2026-01-31", "2026-02-28", "2026-03-01,2026-03-15", "2026-03-15", "2026-03-31")]          // spec 8
    [InlineData("Yearly", "yearly", "2028-02-29", "2029-02-28", "2029-03-01,2029-06-01", "2029-06-01", "2030-02-28")]            // spec 9
    public async Task AMissedPm_StaysOneOverduePm_ThenOneHiddenSuccessor_VisibleOnItsDate(
        string frequency, string bucket, string start, string due, string missedDays, string completedOn, string next)
    {
        var data = new MachinePmScenario();
        var c = data.Checklist(frequency, DateOnly.Parse(start));
        var pm = data.Open(c, DateOnly.Parse(due));
        var s = Create(data, DateOnly.Parse(due));

        var onDue = Assert.Single(await Tab(s, bucket));
        Assert.Equal((pm.PmNo, false), (onDue.PmNo, onDue.IsOverdue)); // due today: visible, not overdue

        foreach (var day in missedDays.Split(',').Select(DateOnly.Parse))
        {
            s.Clock.SetToday(day);
            var shown = Assert.Single(await Tab(s, bucket));
            Assert.Equal((pm.PmNo, "Scheduled", true), (shown.PmNo, shown.Status, shown.IsOverdue)); // same PM, stored status unchanged, overdue
            Assert.Single(s.Data.Pms, p => p.ChecklistId == c.ChecklistId);                         // no PM for the missed day
        }

        s.Clock.SetToday(DateOnly.Parse(completedOn));
        var done = await Complete(s, pm);

        Assert.Equal((MachinePmStatus.Completed, DateOnly.Parse(completedOn)), (done.Status, done.CompletedDate!.Value));
        Assert.Equal(2, s.Data.Pms.Count(p => p.ChecklistId == c.ChecklistId)); // exactly ONE successor
        var successor = Successor(s, c);
        Assert.Equal(DateOnly.Parse(next), successor.ScheduledDate);
        Assert.Empty(await Tab(s, bucket));                                     // hidden on the completion day

        s.Clock.SetToday(DateOnly.Parse(next).AddDays(-1));
        Assert.Empty(await Tab(s, bucket));
        s.Clock.SetToday(DateOnly.Parse(next));
        Assert.Equal(successor.PmNo, Assert.Single(await Tab(s, bucket)).PmNo); // visible on its date
    }

    [Fact]
    public async Task ADailyChain_OnePmVisiblePerDay()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Daily", D(2026, 9, 25));
        data.Open(c, D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 25));

        for (var day = D(2026, 9, 25); day <= D(2026, 9, 27); day = day.AddDays(1))
        {
            s.Clock.SetToday(day);
            var due = Assert.Single(await Tab(s, "daily"));
            Assert.Equal(day, due.ScheduledDate);
            await Complete(s, s.Repo.StoredByNo(due.PmNo));
            Assert.Empty(await Tab(s, "daily")); // tomorrow's PM exists but is hidden
        }

        Assert.Equal(D(2026, 9, 28), Successor(s, c).ScheduledDate);
    }

    [Fact]
    public void ListQuery_DefaultsToTenPerPage()
    {
        Assert.Equal((1, 10), (new MachinePmListQuery().PageNumber, new MachinePmListQuery().PageSize));
    }

    [Fact]
    public async Task GetById_ShowsChecklistNameFrequencyAndMaintenanceBy_Lookups_AreActiveMachinesOnly()
    {
        var data = new MachinePmScenario();
        var c = data.Checklist("Weekly", D(2026, 9, 25));
        var pm = data.Completed(c, D(2026, 9, 25));
        var s = Create(data, D(2026, 9, 25));

        var dto = await s.Service.GetByIdAsync(pm.MachinePmId, CancellationToken.None);

        Assert.Equal((c.ChecklistName, "Weekly", "Ravi", "MAC-0001"), (dto.ChecklistName, dto.Frequency, dto.MaintenanceBy, dto.MachineCode));
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.GetByIdAsync(999, CancellationToken.None));
        Assert.Equal(new[] { 1, 2 }, (await s.Service.GetLookupsAsync(CancellationToken.None)).Machines.Select(m => m.MachineId));
    }

    // ================================================================ rules / clock

    [Theory]
    [InlineData("Maintenance", "Running")]
    [InlineData("Running", "Running")]
    [InlineData("Breakdown", "Breakdown")]
    [InlineData("Idle", "Idle")]
    public void OperationalStatusAfterCompletion_OnlyMaintenanceBecomesRunning(string before, string after)
    {
        Assert.Equal(after, MachinePmRules.OperationalStatusAfterCompletion(before));
    }

    [Fact]
    public void NextMaintenanceDate_IsTheEarliestOpenOccurrence_OrNull()
    {
        Assert.Equal(D(2026, 9, 26), MachinePmRules.NextMaintenanceDateFromOpenOccurrences(new[] { D(2026, 10, 2), D(2026, 9, 26) }));
        Assert.Null(MachinePmRules.NextMaintenanceDateFromOpenOccurrences(Array.Empty<DateOnly>()));
    }

    [Theory]
    [InlineData("2026-09-24T18:29:59", "2026-09-24")]
    [InlineData("2026-09-24T18:30:00", "2026-09-25")]
    public void PlantToday_IsTheIndiaStandardTimeDate(string utc, string expected)
    {
        var instant = DateTime.SpecifyKind(DateTime.Parse(utc, System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);
        Assert.Equal(DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), DateTimeProvider.PlantDate(instant));
    }

    private static void AssertUntouched(Sut s, MachinePm pm)
    {
        var stored = s.Repo.Stored(pm.MachinePmId);
        Assert.Equal((MachinePmStatus.Scheduled, (DateOnly?)null, (string?)null), (stored.Status, stored.CompletedDate, stored.MaintenanceBy));
        Assert.DoesNotContain(s.Data.Pms, p => p != stored && p.ChecklistId == stored.ChecklistId && p.Status == MachinePmStatus.Scheduled);
        Assert.Equal(D(2026, 2, 1), s.Repo.StoredMachine(1).LastMaintenanceDate);
        Assert.Empty(s.Audit.Entries);
    }

    private sealed class ThrowingAuditRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit persistence failure.");
    }
}
