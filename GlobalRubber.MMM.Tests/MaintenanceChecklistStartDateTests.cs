using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Domain.Rules;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Migration 013: the checklist's Start Date is the recurring cycle's permanent anchor and the first occurrence's due date.
/// Plant "today" is 2026-09-25 unless a test moves the clock. Machines: 1, 2 active; 3 inactive. The fake's legacy data
/// has MPM-0001..0005 completed and MPM-0006 open on machine 1, so the next number is MPM-0007.
/// </summary>
public class MaintenanceChecklistStartDateTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private sealed record Sut(MaintenanceChecklistService Service, InMemoryMaintenanceChecklistRepository Repo, RecordingAuditLog Audit, SettableClock Clock);

    private static Sut Create(DateOnly? today = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machineList = MachineTestData.Machines();
        var pms = Enumerable.Range(1, 6).Select(n => new MachinePm
        {
            MachinePmId = n, PmNo = $"MPM-{n:0000}", MachineId = 1, ChecklistId = 3, ScheduledDate = D(2026, 2, 20),
            CompletedDate = n < 6 ? D(2026, 2, 20) : null, Status = n < 6 ? MachinePmStatus.Completed : MachinePmStatus.Scheduled, RowVersion = new byte[] { 1 },
        }).ToList();
        var repo = new InMemoryMaintenanceChecklistRepository(MaintenanceChecklistTestData.Checklists(), machineList, pms);
        var audit = new RecordingAuditLog();
        var clock = new SettableClock(today ?? D(2026, 9, 25));
        var service = new MaintenanceChecklistService(repo, new InMemoryMachineRepository(machineList, departments, employees), users, clock, audit, NullLogger<MaintenanceChecklistService>.Instance);
        return new Sut(service, repo, audit, clock);
    }

    private static CreateMaintenanceChecklistRequest New(string frequency = "Daily", DateOnly? startDate = null, string appliesTo = "Machine", int? machineId = 2, bool noStart = false) => new()
    {
        ChecklistName = $"Press {frequency}", AppliesTo = appliesTo, Frequency = frequency, MachineId = appliesTo == "Mold" ? null : machineId,
        StartDate = noStart ? null : startDate ?? D(2026, 9, 25),
        Items = new[] { "Oil level checked", "Safety guard checked" }.Select(l => new MaintenanceChecklistItemRequest { ItemLabel = l }).ToList(),
    };

    private static UpdateMaintenanceChecklistRequest Edit(Sut s, int id, string? frequency = null, DateOnly? startDate = null, string? appliesTo = null, int? machineId = -1, bool clearStart = false)
    {
        var c = s.Repo.Stored(id);
        var applies = appliesTo ?? c.AppliesTo;
        return new UpdateMaintenanceChecklistRequest
        {
            ChecklistName = c.ChecklistName, AppliesTo = applies, Frequency = frequency ?? c.Frequency,
            MachineId = machineId == -1 ? (applies == "Mold" ? null : c.MachineId) : machineId,
            StartDate = clearStart ? null : startDate ?? c.StartDate,
            Items = c.Items.OrderBy(i => i.SortOrder).Select(i => new MaintenanceChecklistItemRequest { ItemLabel = i.ItemLabel }).ToList(),
            RowVersion = Convert.ToBase64String(c.RowVersion),
        };
    }

    private static MachinePm Open(Sut s, int checklistId) => Assert.Single(s.Repo.Pms, p => p.ChecklistId == checklistId && p.Status == MachinePmStatus.Scheduled);

    // ================================================================ create: the first occurrence is due ON the start date

    [Theory]
    [InlineData("Daily")]
    [InlineData("Weekly")]
    [InlineData("Monthly")]
    [InlineData("Yearly")]
    public async Task TheFirstOccurrence_IsDueOnTheStartDate_ForEveryFrequency(string frequency)
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(New(frequency, D(2026, 10, 3)), 1, null, CancellationToken.None);

        var pm = Open(s, dto.ChecklistId);
        Assert.Equal((D(2026, 10, 3), "MPM-0007", 2, MachinePmStatus.Scheduled), (pm.ScheduledDate, pm.PmNo, pm.MachineId, pm.Status));
        Assert.Null(pm.MaintenanceTypeId);
        Assert.Null(pm.EngineerId);
        Assert.Null(pm.MaintenanceBy);
        Assert.Null(pm.CompletedDate);
        Assert.Null(pm.Remarks);
        Assert.Equal(new[] { "Oil level checked", "Safety guard checked" }, pm.ChecklistItems.Select(i => i.ItemLabel));
        Assert.Equal(D(2026, 10, 3), dto.StartDate);
        Assert.Equal(D(2026, 10, 3), s.Repo.Stored(dto.ChecklistId).StartDate);
        Assert.Equal(D(2026, 10, 3), s.Repo.StoredMachine(2).NextMaintenanceDate); // its only open occurrence
    }

    [Fact]
    public async Task AStartDateToday_MakesTheFirstOccurrenceDueToday()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(New("Daily", D(2026, 9, 25)), 1, null, CancellationToken.None);

        Assert.Equal(D(2026, 9, 25), Open(s, dto.ChecklistId).ScheduledDate);
    }

    [Fact]
    public async Task APastStartDate_IsTheFirstDueDate_TheOccurrenceIsSimplyOverdue_NoBacklog()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(New("Daily", D(2026, 9, 20)), 1, null, CancellationToken.None);

        Assert.Equal(D(2026, 9, 20), Open(s, dto.ChecklistId).ScheduledDate);
        Assert.Single(s.Repo.Pms, p => p.ChecklistId == dto.ChecklistId); // one occurrence, not 20..25
    }

    [Fact]
    public async Task TheStartDate_NotTheCreationDate_IsTheAnchor()
    {
        var s = Create();
        var dto = await s.Service.CreateAsync(New("Weekly", D(2026, 9, 21)), 1, null, CancellationToken.None); // created 25-Sep, a Friday

        Assert.Equal(D(2026, 9, 21), s.Repo.Stored(dto.ChecklistId).CycleAnchor());
        Assert.Equal(D(2026, 9, 28), RecurrenceRules.FirstAfter(s.Repo.Stored(dto.ChecklistId).CycleAnchor(), "Weekly", D(2026, 9, 25))); // Mondays
    }

    [Fact]
    public void ALegacyChecklistWithoutAStartDate_FallsBackToItsCreationDateInIst()
    {
        var legacy = new MaintenanceChecklist { CreatedAt = new DateTime(2026, 9, 24, 20, 0, 0, DateTimeKind.Utc) }; // 25-Sep 01:30 IST

        Assert.Equal(D(2026, 9, 25), legacy.CycleAnchor());
    }

    [Fact]
    public async Task AMachineChecklistWithoutAStartDate_Is400_NothingWritten()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(New(noStart: true), 1, null, CancellationToken.None));

        Assert.Equal(new[] { "StartDate is required for a Machine checklist." }, ex.Errors);
        Assert.Equal(0, s.Repo.AddCalls);
        Assert.Equal(6, s.Repo.Pms.Count);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task AMoldChecklist_MayOmitOrKeepAStartDate_AndNeverCreatesAMachinePm()
    {
        var s = Create();

        var without = await s.Service.CreateAsync(New(appliesTo: "Mold", noStart: true), 1, null, CancellationToken.None);
        var with = await s.Service.CreateAsync(new CreateMaintenanceChecklistRequest
        {
            ChecklistName = "Mold weekly", AppliesTo = "Mold", Frequency = "Weekly", StartDate = D(2026, 10, 1),
            Items = new[] { new MaintenanceChecklistItemRequest { ItemLabel = "Cavities cleaned" } },
        }, 1, null, CancellationToken.None);

        Assert.Null(without.StartDate);
        Assert.Equal(D(2026, 10, 1), with.StartDate);
        Assert.Equal(6, s.Repo.Pms.Count);
    }

    [Fact]
    public async Task TheCreationAudit_RecordsTheStartDate()
    {
        var s = Create();

        await s.Service.CreateAsync(New("Weekly", D(2026, 10, 2)), 1, null, CancellationToken.None);

        Assert.Contains("starting 2026-10-02", s.Audit.Entries[0].Description);
        Assert.Contains("on 2026-10-02", s.Audit.Entries[1].Description); // MachinePmScheduled
    }

    // ================================================================ edit

    [Fact]
    public async Task ChangingTheStartDate_ReAnchors_AndReDatesTheOpenOccurrence_ToTheFirstNewCycleDateOnOrAfterToday()
    {
        var s = Create();
        var dto = await s.Service.CreateAsync(New("Weekly", D(2026, 9, 25)), 1, null, CancellationToken.None); // due 25-Sep (Fri)
        s.Clock.SetToday(D(2026, 9, 29));
        s.Audit.Entries.Clear();

        await s.Service.UpdateAsync(dto.ChecklistId, Edit(s, dto.ChecklistId, startDate: D(2026, 9, 21)), 1, null, CancellationToken.None); // Mondays

        var open = Open(s, dto.ChecklistId);
        Assert.Equal(("MPM-0007", D(2026, 10, 5)), (open.PmNo, open.ScheduledDate)); // first Monday on/after the 29th; same PM, no new one
        Assert.Equal(D(2026, 9, 21), s.Repo.Stored(dto.ChecklistId).StartDate);
        Assert.Equal(D(2026, 10, 5), s.Repo.StoredMachine(2).NextMaintenanceDate);
        var audit = Assert.Single(s.Audit.Entries);
        Assert.Contains(audit.Details, d => d is { FieldName: "start_date", OldValue: "2026-09-25", NewValue: "2026-09-21" });
        Assert.Contains(audit.Details, d => d is { FieldName: "open_pm_scheduled_date (MPM-0007)", OldValue: "2026-09-25", NewValue: "2026-10-05" });
    }

    [Fact]
    public async Task MovingTheStartDateIntoTheFuture_MakesTheOpenOccurrenceDueOnIt()
    {
        var s = Create();
        var dto = await s.Service.CreateAsync(New("Monthly", D(2026, 9, 25)), 1, null, CancellationToken.None);

        await s.Service.UpdateAsync(dto.ChecklistId, Edit(s, dto.ChecklistId, startDate: D(2026, 11, 1)), 1, null, CancellationToken.None);

        Assert.Equal(D(2026, 11, 1), Open(s, dto.ChecklistId).ScheduledDate);
    }

    [Fact]
    public async Task AFrequencyChange_CountsFromTheStartDate_NotTheCreationDate()
    {
        var s = Create();
        var dto = await s.Service.CreateAsync(New("Daily", D(2026, 9, 25)), 1, null, CancellationToken.None);
        s.Repo.Stored(dto.ChecklistId).CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc); // must not matter
        s.Clock.SetToday(D(2026, 9, 29));

        await s.Service.UpdateAsync(dto.ChecklistId, Edit(s, dto.ChecklistId, frequency: "Weekly"), 1, null, CancellationToken.None);

        Assert.Equal(D(2026, 10, 2), Open(s, dto.ChecklistId).ScheduledDate); // the prompt's example: anchor 25-Sep, today 29-Sep -> 02-Oct
    }

    [Fact]
    public async Task ClearingTheStartDateOfAnActiveMachineChecklist_Is400_NothingChanged()
    {
        var s = Create();
        var dto = await s.Service.CreateAsync(New("Daily", D(2026, 9, 25)), 1, null, CancellationToken.None);
        s.Audit.Entries.Clear();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(dto.ChecklistId, Edit(s, dto.ChecklistId, clearStart: true), 1, null, CancellationToken.None));

        Assert.Contains("StartDate is required for a Machine checklist.", ex.Errors);
        Assert.Equal(D(2026, 9, 25), s.Repo.Stored(dto.ChecklistId).StartDate);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task MoldToMachine_RequiresAStartDate_ThenCreatesTheOccurrenceFromIt()
    {
        var s = Create();
        var mold = await s.Service.CreateAsync(New("Weekly", appliesTo: "Mold", noStart: true), 1, null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(mold.ChecklistId, Edit(s, mold.ChecklistId, appliesTo: "Machine", machineId: 2), 1, null, CancellationToken.None));
        Assert.Contains("StartDate is required for a Machine checklist.", ex.Errors);
        Assert.Equal(6, s.Repo.Pms.Count);

        s.Clock.SetToday(D(2026, 9, 29));
        await s.Service.UpdateAsync(mold.ChecklistId, Edit(s, mold.ChecklistId, appliesTo: "Machine", machineId: 2, startDate: D(2026, 9, 25)), 1, null, CancellationToken.None);

        Assert.Equal((D(2026, 10, 2), 2), (Open(s, mold.ChecklistId).ScheduledDate, Open(s, mold.ChecklistId).MachineId)); // first Friday on/after the 29th - no backlog
    }

    [Fact]
    public async Task EditingAnInactiveChecklist_DoesNotRequireAStartDate()
    {
        var s = Create();

        var dto = await s.Service.UpdateAsync(3, Edit(s, 3, clearStart: true), 1, null, CancellationToken.None); // legacy CHK-0003, inactive

        Assert.Null(dto.StartDate);
    }
}
