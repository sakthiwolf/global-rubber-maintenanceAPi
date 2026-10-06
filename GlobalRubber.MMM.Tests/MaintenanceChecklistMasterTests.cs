using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Preventive Maintenance Plan -> Checklist Master (migration 020) through the real MaintenanceChecklistService against the
/// in-memory fakes. Plans 1 (Machine, own items - created before 020), 2 (Mold), 3 (Machine, inactive). Checklist Masters:
/// 901 Machine Weekly Checks (2 items), 902 Retired Checks (INACTIVE), 903 Mold Checks (Mold), 904 Empty Checks (no items),
/// 905 Weekly Machine Maintenance (4 items). Machines 1, 2 active. Acting user 1 "Sakthi"; plant today 2026-03-01.
/// </summary>
public class MaintenanceChecklistMasterTests
{
    private const int Weekly = MaintenanceChecklistTestData.WeeklyMasterId;
    private const int Default = MaintenanceChecklistTestData.DefaultMasterId;
    private static readonly string[] WeeklyLabels = { "Oil level checked", "Lubrication checked", "Belt condition checked", "Safety guard checked" };

    private sealed record Sut(MaintenanceChecklistService Service, InMemoryMaintenanceChecklistRepository Repo, RecordingAuditLog Audit);

    private static Sut Create()
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machineList = MachineTestData.Machines();
        var repo = new InMemoryMaintenanceChecklistRepository(MaintenanceChecklistTestData.All(), machineList);
        var audit = new RecordingAuditLog();
        var service = new MaintenanceChecklistService(repo, new InMemoryMachineRepository(machineList, departments, employees),
            new InMemoryMaintenanceTypeRepository(MaintenanceTypeTestData.MaintenanceTypes()), users, new FixedClock(), audit,
            new RecordingNotificationPublisher(), NullLogger<MaintenanceChecklistService>.Instance);
        return new Sut(service, repo, audit);
    }

    private static List<MaintenanceChecklistItemRequest> Items(params string[] labels) => labels.Select(l => new MaintenanceChecklistItemRequest { ItemLabel = l }).ToList();

    private static CreateMaintenanceChecklistRequest Plan(int? sourceId = Weekly, string name = "Press 2 Weekly", List<MaintenanceChecklistItemRequest>? items = null) => new()
    {
        ChecklistName = name, AppliesTo = "Machine", Frequency = "Weekly", MachineId = 2, StartDate = new DateOnly(2026, 3, 2),
        SourceChecklistId = sourceId, Items = items,
    };

    private static UpdateMaintenanceChecklistRequest EditPlan(Sut s, int id, int? sourceId, List<MaintenanceChecklistItemRequest>? items = null, string? name = null)
    {
        var c = s.Repo.Stored(id);
        return new UpdateMaintenanceChecklistRequest
        {
            ChecklistName = name ?? c.ChecklistName, AppliesTo = c.AppliesTo, Frequency = c.Frequency, MachineId = c.MachineId, StartDate = c.StartDate,
            SourceChecklistId = sourceId, Items = items, RowVersion = Convert.ToBase64String(c.RowVersion),
        };
    }

    private static MachinePm OpenPm(Sut s, int planId) => Assert.Single(s.Repo.Pms, p => p.ChecklistId == planId && p.Status == MachinePmStatus.Scheduled);

    private static async Task<ValidationException> Invalid(Func<Task> act) => await Assert.ThrowsAsync<ValidationException>(act);

    // ================================================================ dropdown: active checklist masters load

    [Fact]
    public async Task Options_ListOnlyActiveMachineChecklistMasters_ByCode_WithTheirItems_NeverPlans()
    {
        var options = await Create().Service.GetChecklistMasterOptionsAsync("Machine", CancellationToken.None);

        Assert.Equal(new[] { "CHK-0901", "CHK-0904", "CHK-0905" }, options.Select(o => o.ChecklistCode)); // 902 inactive, 903 Mold, plans excluded
        var weekly = options.Single(o => o.ChecklistId == Weekly);
        Assert.Equal("Weekly Machine Maintenance", weekly.ChecklistName);
        Assert.Equal(WeeklyLabels, weekly.Items.Select(i => i.ItemLabel));
        Assert.Equal(new[] { 1, 2, 3, 4 }, weekly.Items.Select(i => i.SortOrder));
    }

    [Fact]
    public async Task Options_DefaultToMachine_FilterMold_AndRejectAnUnknownAppliesTo()
    {
        var s = Create();

        Assert.Equal(3, (await s.Service.GetChecklistMasterOptionsAsync(null, CancellationToken.None)).Count);
        Assert.Equal(new[] { "CHK-0903" }, (await s.Service.GetChecklistMasterOptionsAsync("mold", CancellationToken.None)).Select(o => o.ChecklistCode));
        var ex = await Invalid(() => s.Service.GetChecklistMasterOptionsAsync("Tool", CancellationToken.None));
        Assert.Contains("AppliesTo must be one of: Machine, Mold.", ex.Errors);
    }

    [Fact]
    public async Task PlansAndChecklistMasters_AreListedSeparately()
    {
        var s = Create();
        var query = new MaintenanceChecklistListQuery { PageNumber = 1, PageSize = 50 };

        var plans = await s.Service.GetAllAsync(query, CancellationToken.None);
        var masters = await s.Service.GetChecklistMastersAsync(query, CancellationToken.None);

        Assert.Equal(new[] { "CHK-0001", "CHK-0002", "CHK-0003" }, plans.Items.Select(p => p.ChecklistCode).OrderBy(c => c));
        Assert.All(plans.Items, p => Assert.False(p.IsChecklistMaster));
        Assert.Equal(5, masters.TotalCount);
        Assert.All(masters.Items, m => Assert.True(m.IsChecklistMaster));
        Assert.All(masters.Items, m => Assert.Null(m.Frequency));
    }

    // ================================================================ create a PM plan with a checklist master

    [Fact]
    public async Task CreatePlan_WithAChecklistMaster_UsesItsItems_StoresNoCopy_AndTheFirstPmSnapshotsTheMasterItems()
    {
        var s = Create();
        var masterItemsBefore = s.Repo.Stored(Weekly).Items.Count;

        var dto = await s.Service.CreateAsync(Plan(), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal((Weekly, "CHK-0905", "Weekly Machine Maintenance", (bool?)true), (dto.SourceChecklistId!.Value, dto.SourceChecklistCode, dto.SourceChecklistName, dto.SourceChecklistIsActive));
        Assert.False(dto.IsChecklistMaster);
        Assert.Equal(WeeklyLabels, dto.Items.Select(i => i.ItemLabel));                 // shown read-only from the master
        Assert.Empty(s.Repo.Stored(dto.ChecklistId).Items);                             // no duplicate items on the plan
        Assert.Equal(masterItemsBefore, s.Repo.Stored(Weekly).Items.Count);             // the master is untouched
        var pm = OpenPm(s, dto.ChecklistId);
        Assert.Equal(WeeklyLabels, pm.ChecklistItems.Select(l => l.ItemLabel));
        Assert.Equal(s.Repo.Stored(Weekly).Items.Select(i => (int?)i.ChecklistItemId), pm.ChecklistItems.Select(l => l.ChecklistItemId)); // master item ids
        Assert.Contains("using checklist master CHK-0905 (4 items).", s.Audit.Entries[0].Description);
    }

    [Fact]
    public async Task TwoPlansCanShareOneMaster_WithoutDuplicatingItsItems()
    {
        var s = Create();

        var a = await s.Service.CreateAsync(Plan(name: "Press 2 Weekly A"), 1, null, CancellationToken.None);
        var b = await s.Service.CreateAsync(Plan(name: "Press 2 Weekly B"), 1, null, CancellationToken.None);

        Assert.Empty(s.Repo.Stored(a.ChecklistId).Items);
        Assert.Empty(s.Repo.Stored(b.ChecklistId).Items);
        Assert.Equal(4, s.Repo.Stored(Weekly).Items.Count);
        Assert.Equal(WeeklyLabels, b.Items.Select(i => i.ItemLabel));
    }

    // ================================================================ validation of the selected master

    [Fact]
    public async Task AnInactiveChecklistMaster_CannotBeSelected()
    {
        var s = Create();

        var ex = await Invalid(() => s.Service.CreateAsync(Plan(MaintenanceChecklistTestData.InactiveMasterId), 1, null, CancellationToken.None));

        Assert.Contains("The selected Checklist Master is not active.", ex.Errors);
        Assert.Equal(0, s.Repo.AddCalls);
        Assert.Empty(s.Repo.Pms);
    }

    [Fact]
    public async Task AChecklistMasterWithNoItems_IsRejected()
    {
        var s = Create();

        var ex = await Invalid(() => s.Service.CreateAsync(Plan(MaintenanceChecklistTestData.EmptyMasterId), 1, null, CancellationToken.None));

        Assert.Contains("The selected Checklist Master has no checklist items.", ex.Errors);
        Assert.Equal(0, s.Repo.AddCalls);
    }

    [Fact]
    public async Task TheMaster_MustExist_BeAChecklistMaster_AndApplyToMachine()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(Plan(4242), 1, null, CancellationToken.None));
        var plan = await Invalid(() => s.Service.CreateAsync(Plan(1), 1, null, CancellationToken.None));            // plan 1 is a PLAN
        Assert.Contains("The selected checklist is not a Checklist Master.", plan.Errors);
        var mold = await Invalid(() => s.Service.CreateAsync(Plan(MaintenanceChecklistTestData.MoldMasterId), 1, null, CancellationToken.None));
        Assert.Contains("The selected Checklist Master does not apply to Machine.", mold.Errors);
        var bad = await Invalid(() => s.Service.CreateAsync(Plan(0), 1, null, CancellationToken.None));
        Assert.Contains("SourceChecklistId is not valid.", bad.Errors);
        Assert.Equal(0, s.Repo.AddCalls);
    }

    [Fact]
    public async Task AMachinePlan_RequiresAMaster_AndNeverAcceptsItsOwnItemsNextToOne()
    {
        var s = Create();

        var missing = await Invalid(() => s.Service.CreateAsync(Plan(sourceId: null, items: Items("Oil level checked")), 1, null, CancellationToken.None));
        Assert.Contains("Checklist Master is required for a Machine plan.", missing.Errors);

        // Arbitrary items (with or without ids) next to a master are refused - the items always come from the master.
        var mixed = await Invalid(() => s.Service.CreateAsync(Plan(items: Items("Something else")), 1, null, CancellationToken.None));
        Assert.Contains("Checklist items come from the selected Checklist Master and cannot be entered on the plan.", mixed.Errors);

        // Blank rows are dropped first, so a form that sends an empty row is still fine.
        await s.Service.CreateAsync(Plan(items: Items("  ")), 1, null, CancellationToken.None);
        Assert.Equal(1, s.Repo.AddCalls);
    }

    [Fact]
    public async Task AMoldPlan_CannotUseAChecklistMaster_AndKeepsItsOwnItems()
    {
        var s = Create();

        var ex = await Invalid(() => s.Service.CreateAsync(new CreateMaintenanceChecklistRequest
        {
            ChecklistName = "Mold weekly", AppliesTo = "Mold", Frequency = "Weekly", SourceChecklistId = MaintenanceChecklistTestData.MoldMasterId, Items = Items("Cavity cleaned"),
        }, 1, null, CancellationToken.None));
        Assert.Contains("A Mold plan cannot use a Checklist Master.", ex.Errors);

        var dto = await s.Service.CreateAsync(new CreateMaintenanceChecklistRequest
        {
            ChecklistName = "Mold weekly", AppliesTo = "Mold", Frequency = "Weekly", Items = Items("Cavity cleaned"),
        }, 1, null, CancellationToken.None);
        Assert.Null(dto.SourceChecklistId);
        Assert.Equal(new[] { "Cavity cleaned" }, s.Repo.Stored(dto.ChecklistId).Items.Select(i => i.ItemLabel));
    }

    // ================================================================ edit: load the saved master / change it

    [Fact]
    public async Task EditLoad_ReturnsTheSavedMasterAndItsCurrentItems()
    {
        var s = Create();
        var created = await s.Service.CreateAsync(Plan(), 1, null, CancellationToken.None);
        s.Repo.Stored(Weekly).Items[0].ItemLabel = "Oil level checked (topped up)"; // the master is edited later

        var dto = await s.Service.GetByIdAsync(created.ChecklistId, CancellationToken.None);

        Assert.Equal((Weekly, "CHK-0905"), (dto.SourceChecklistId!.Value, dto.SourceChecklistCode));
        Assert.Equal("Oil level checked (topped up)", dto.Items[0].ItemLabel); // the master's CURRENT items
        Assert.Equal(4, dto.Items.Count);
    }

    [Fact]
    public async Task ChangingTheMaster_ReplacesTheOpenPmItems_WithoutMixingTheTwoMasters()
    {
        var s = Create();
        var created = await s.Service.CreateAsync(Plan(), 1, null, CancellationToken.None);
        var pmNo = OpenPm(s, created.ChecklistId).PmNo;
        s.Audit.Entries.Clear();

        var dto = await s.Service.UpdateAsync(created.ChecklistId, EditPlan(s, created.ChecklistId, Default), 1, null, CancellationToken.None);

        Assert.Equal((Default, "CHK-0901"), (dto.SourceChecklistId!.Value, dto.SourceChecklistCode));
        Assert.Equal(new[] { "Hoses checked", "Pressure recorded" }, dto.Items.Select(i => i.ItemLabel));
        var open = OpenPm(s, created.ChecklistId);
        Assert.Equal(pmNo, open.PmNo);                                                                 // same occurrence
        Assert.Equal(new[] { "Hoses checked", "Pressure recorded" }, open.ChecklistItems.Select(l => l.ItemLabel)); // only the new master's
        Assert.Empty(s.Repo.Stored(created.ChecklistId).Items);
        var entry = Assert.Single(s.Audit.Entries);
        Assert.Contains(entry.Details, d => d is { FieldName: "checklist_master", OldValue: "CHK-0905", NewValue: "CHK-0901" });
        Assert.Contains($"open PM {pmNo} checklist refreshed", entry.Description);
    }

    [Fact]
    public async Task KeepingTheSameMaster_DoesNotRewriteAnything_AndDuplicatesNoItems()
    {
        var s = Create();
        var created = await s.Service.CreateAsync(Plan(), 1, null, CancellationToken.None);
        var snapshotBefore = OpenPm(s, created.ChecklistId).ChecklistItems.Select(l => l.MachinePmChecklistId).ToList();

        await s.Service.UpdateAsync(created.ChecklistId, EditPlan(s, created.ChecklistId, Weekly, name: "Press 2 Weekly (renamed)"), 1, null, CancellationToken.None);

        Assert.False(s.Repo.LastReplaceItems);
        Assert.Empty(s.Repo.Stored(created.ChecklistId).Items);
        Assert.Equal(4, s.Repo.Stored(Weekly).Items.Count);
        Assert.Equal(snapshotBefore, OpenPm(s, created.ChecklistId).ChecklistItems.Select(l => l.MachinePmChecklistId)); // snapshot untouched
    }

    [Fact]
    public async Task AnUnchangedMasterDeactivatedSince_StaysAssigned_ButCannotBeNewlyChosen()
    {
        var s = Create();
        var created = await s.Service.CreateAsync(Plan(), 1, null, CancellationToken.None);
        s.Repo.Stored(Weekly).IsActive = false;

        var dto = await s.Service.UpdateAsync(created.ChecklistId, EditPlan(s, created.ChecklistId, Weekly, name: "Renamed"), 1, null, CancellationToken.None);
        Assert.Equal(((int?)Weekly, (bool?)false), (dto.SourceChecklistId, dto.SourceChecklistIsActive));

        var ex = await Invalid(() => s.Service.UpdateAsync(1, EditPlan(s, 1, Weekly), 1, null, CancellationToken.None));
        Assert.Contains("The selected Checklist Master is not active.", ex.Errors);
    }

    [Fact]
    public async Task ClearingTheMasterOfAPlanThatHasOne_IsRejected()
    {
        var s = Create();
        var created = await s.Service.CreateAsync(Plan(), 1, null, CancellationToken.None);

        var ex = await Invalid(() => s.Service.UpdateAsync(created.ChecklistId, EditPlan(s, created.ChecklistId, sourceId: null, items: Items("Typed item")), 1, null, CancellationToken.None));

        Assert.Contains("Checklist Master is required for a Machine plan.", ex.Errors);
        Assert.Equal((int?)Weekly, s.Repo.Stored(created.ChecklistId).SourceChecklistId);
    }

    // ================================================================ existing plans are preserved (no automatic conversion)

    [Fact]
    public async Task AnOlderPlan_WithoutAMaster_KeepsItsOwnItems_OnEdit()
    {
        var s = Create();
        var own = s.Repo.Stored(1).Items.Select(i => i.ItemLabel).ToArray();

        var dto = await s.Service.UpdateAsync(1, EditPlan(s, 1, sourceId: null, items: Items(own), name: "Machine Lubrication v2"), 1, null, CancellationToken.None);

        Assert.Null(dto.SourceChecklistId);
        Assert.Equal(own, dto.Items.Select(i => i.ItemLabel));
        Assert.Equal(own, s.Repo.Stored(1).Items.Select(i => i.ItemLabel));
        Assert.False(s.Repo.LastReplaceItems); // the item rows (and the ids PM history points at) are kept
    }

    [Fact]
    public async Task ChoosingAMasterForAnOlderPlan_RemovesItsOwnItems_SoNothingIsDuplicated()
    {
        var s = Create();

        var dto = await s.Service.UpdateAsync(1, EditPlan(s, 1, Weekly), 1, null, CancellationToken.None);

        Assert.Equal((int?)Weekly, dto.SourceChecklistId);
        Assert.Equal(WeeklyLabels, dto.Items.Select(i => i.ItemLabel));
        Assert.True(s.Repo.LastReplaceItems);
        Assert.Empty(s.Repo.Stored(1).Items);
        Assert.Equal(4, s.Repo.Stored(Weekly).Items.Count);
    }

    // ================================================================ checklist master maintenance

    [Fact]
    public async Task CreateChecklistMaster_HasNoPlanConfiguration_SchedulesNothing_AndIsOffered()
    {
        var s = Create();

        var dto = await s.Service.CreateChecklistMasterAsync(new CreateChecklistMasterRequest
        {
            ChecklistName = "Monthly Hydraulics", AppliesTo = "machine", Items = Items("Hoses checked", " ", "Pressure recorded"),
        }, 1, null, CancellationToken.None);

        Assert.True(dto.IsChecklistMaster);
        Assert.Equal(("CHK-0004", "Machine"), (dto.ChecklistCode, dto.AppliesTo));
        Assert.Equal((null, null, null, null, null), (dto.Frequency, dto.MachineId, dto.StartDate, dto.MaintenanceTypeId, dto.SourceChecklistId));
        Assert.Equal(new[] { "Hoses checked", "Pressure recorded" }, dto.Items.Select(i => i.ItemLabel));
        Assert.Empty(s.Repo.Pms);
        Assert.Contains("created checklist master 'Monthly Hydraulics' (CHK-0004) for Machine with 2 items.", Assert.Single(s.Audit.Entries).Description);
        Assert.Contains((await s.Service.GetChecklistMasterOptionsAsync("Machine", CancellationToken.None)), o => o.ChecklistId == dto.ChecklistId);
    }

    [Fact]
    public async Task CreateChecklistMaster_ValidatesNameAppliesToAndItems()
    {
        var s = Create();

        var ex = await Invalid(() => s.Service.CreateChecklistMasterAsync(new CreateChecklistMasterRequest { ChecklistName = " ", AppliesTo = "Both", Items = Items(" ") }, 1, null, CancellationToken.None));

        Assert.Contains("ChecklistName is required.", ex.Errors);
        Assert.Contains("AppliesTo must be one of: Machine, Mold.", ex.Errors);
        Assert.Contains("Please add at least one checklist item.", ex.Errors);
        await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateChecklistMasterAsync(
            new CreateChecklistMasterRequest { ChecklistName = "machine lubrication", AppliesTo = "Machine", Items = Items("x") }, 1, null, CancellationToken.None));
        Assert.Equal(0, s.Repo.AddCalls);
    }

    [Fact]
    public async Task UpdateChecklistMaster_ReplacesItsItems_AndTheNextPmOfAPlanUsesThem()
    {
        var s = Create();
        var plan = await s.Service.CreateAsync(Plan(), 1, null, CancellationToken.None);
        var openBefore = OpenPm(s, plan.ChecklistId).ChecklistItems.Select(l => l.ItemLabel).ToList();

        var dto = await s.Service.UpdateChecklistMasterAsync(Weekly, new UpdateChecklistMasterRequest
        {
            ChecklistName = "Weekly Machine Maintenance", AppliesTo = "Machine", Items = Items("Oil level checked", "Guard bolts tightened"),
            RowVersion = Convert.ToBase64String(s.Repo.Stored(Weekly).RowVersion),
        }, 1, null, CancellationToken.None);

        Assert.Equal(new[] { "Oil level checked", "Guard bolts tightened" }, dto.Items.Select(i => i.ItemLabel));
        Assert.Equal(new[] { "Oil level checked", "Guard bolts tightened" }, (await s.Service.GetByIdAsync(plan.ChecklistId, CancellationToken.None)).Items.Select(i => i.ItemLabel));
        Assert.Equal(openBefore, OpenPm(s, plan.ChecklistId).ChecklistItems.Select(l => l.ItemLabel)); // an existing PM keeps its snapshot
        Assert.Empty(s.Repo.Stored(plan.ChecklistId).Items);
    }

    [Fact]
    public async Task UpdateChecklistMaster_CannotChangeAppliesTo_WhilePlansUseIt()
    {
        var s = Create();
        await s.Service.CreateAsync(Plan(), 1, null, CancellationToken.None);
        UpdateChecklistMasterRequest ToMold(int id) => new()
        {
            ChecklistName = s.Repo.Stored(id).ChecklistName, AppliesTo = "Mold", Items = Items(s.Repo.EffectiveLabels(id)),
            RowVersion = Convert.ToBase64String(s.Repo.Stored(id).RowVersion),
        };

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateChecklistMasterAsync(Weekly, ToMold(Weekly), 1, null, CancellationToken.None));
        Assert.Contains("used by preventive maintenance plans", ex.Message);
        Assert.Equal("Machine", s.Repo.Stored(Weekly).AppliesTo);

        var unused = await s.Service.UpdateChecklistMasterAsync(Default, ToMold(Default), 1, null, CancellationToken.None);
        Assert.Equal("Mold", unused.AppliesTo);
    }

    [Fact]
    public async Task PlanAndMasterEndpoints_NeverEditTheOtherKind()
    {
        var s = Create();

        var planViaMaster = await Invalid(() => s.Service.UpdateChecklistMasterAsync(1, new UpdateChecklistMasterRequest
        {
            ChecklistName = "x", AppliesTo = "Machine", Items = Items("x"), RowVersion = Convert.ToBase64String(s.Repo.Stored(1).RowVersion),
        }, 1, null, CancellationToken.None));
        Assert.Contains("This is a preventive maintenance plan, not a Checklist Master. Edit it from Preventive Maintenance Plans.", planViaMaster.Errors);

        var masterViaPlan = await Invalid(() => s.Service.UpdateAsync(Weekly, new UpdateMaintenanceChecklistRequest
        {
            ChecklistName = "x", AppliesTo = "Machine", Frequency = "Daily", MachineId = 1, StartDate = new DateOnly(2026, 3, 1), SourceChecklistId = Default,
            RowVersion = Convert.ToBase64String(s.Repo.Stored(Weekly).RowVersion),
        }, 1, null, CancellationToken.None));
        Assert.Contains("This is a Checklist Master, not a preventive maintenance plan. Edit it from Checklist Masters.", masterViaPlan.Errors);
        Assert.Equal(0, s.Repo.UpdateCalls);
    }

    [Fact]
    public async Task DeactivatingAChecklistMaster_RemovesItFromTheDropdown()
    {
        var s = Create();

        await s.Service.DeactivateAsync(Default, 1, null, CancellationToken.None);

        Assert.DoesNotContain(await s.Service.GetChecklistMasterOptionsAsync("Machine", CancellationToken.None), o => o.ChecklistId == Default);
        Assert.Contains("deactivated checklist master 'Machine Weekly Checks' (CHK-0901).", Assert.Single(s.Audit.Entries).Description);
    }
}
