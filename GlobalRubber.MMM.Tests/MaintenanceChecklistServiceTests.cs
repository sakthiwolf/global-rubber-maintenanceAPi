using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The real MaintenanceChecklistService against in-memory fakes. Acting user 1 "Sakthi". Checklists: 1 Machine
/// Lubrication (Machine, 2 items), 2 Mold Cleaning (Mold, 1 item) - active; 3 Old Checklist (Machine) - inactive.
/// </summary>
public class MaintenanceChecklistServiceTests
{
    private sealed record Sut(MaintenanceChecklistService Service, InMemoryMaintenanceChecklistRepository Checklists, RecordingAuditLog Audit, InMemoryUserRepository Users);

    private static Sut Create(IAuditLogService? auditOverride = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machineList = MachineTestData.Machines();
        var checklists = new InMemoryMaintenanceChecklistRepository(MaintenanceChecklistTestData.Checklists(), machineList);
        var audit = new RecordingAuditLog();
        var service = new MaintenanceChecklistService(checklists, new InMemoryMachineRepository(machineList, departments, employees), users,
            new FixedClock(), auditOverride ?? audit, NullLogger<MaintenanceChecklistService>.Instance);
        return new Sut(service, checklists, audit, users);
    }

    private static List<MaintenanceChecklistItemRequest> Items(params string?[] labels) =>
        labels.Select(l => new MaintenanceChecklistItemRequest { ItemLabel = l }).ToList();

    // Recurring configuration defaults: Daily, and machine 1 (active) for a Machine checklist / none for Mold.
    // machineId = -1 means "the default for the applies-to".
    private static CreateMaintenanceChecklistRequest NewChecklist(
        string name = "Hydraulic Inspection", string? appliesTo = "Machine", List<MaintenanceChecklistItemRequest>? items = null,
        string? frequency = "Daily", int? machineId = -1) =>
        new()
        {
            ChecklistName = name, AppliesTo = appliesTo, Items = items ?? Items("Hoses checked", "Pressure recorded"), Frequency = frequency, StartDate = new DateOnly(2026, 3, 1),
            MachineId = machineId == -1 ? (string.Equals(appliesTo?.Trim(), "Machine", StringComparison.OrdinalIgnoreCase) ? 1 : null) : machineId,
        };

    private static UpdateMaintenanceChecklistRequest Edit(
        InMemoryMaintenanceChecklistRepository repo, int id, string? name = null, string? appliesTo = null,
        List<MaintenanceChecklistItemRequest>? items = null, string? rowVersion = null, string? frequency = null, int? machineId = -1) =>
        new()
        {
            ChecklistName = name ?? repo.Stored(id).ChecklistName,
            AppliesTo = appliesTo ?? repo.Stored(id).AppliesTo,
            Frequency = frequency ?? repo.Stored(id).Frequency,
            MachineId = machineId == -1 ? (string.Equals(appliesTo ?? repo.Stored(id).AppliesTo, "Mold", StringComparison.OrdinalIgnoreCase) ? null : repo.Stored(id).MachineId) : machineId,
            StartDate = repo.Stored(id).StartDate ?? new DateOnly(2026, 3, 1),
            Items = items ?? Items(repo.Stored(id).Items.OrderBy(i => i.SortOrder).Select(i => i.ItemLabel).ToArray()),
            RowVersion = rowVersion ?? Convert.ToBase64String(repo.Stored(id).RowVersion),
        };

    // ================================================================ list / get

    [Fact]
    public async Task GetAll_ReturnsAPagedResult_OrderedByName_WithItems()
    {
        var result = await Create().Service.GetAllAsync(new MaintenanceChecklistListQuery { PageNumber = 1, PageSize = 2 }, CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.TotalPages);
        Assert.True(result.HasNextPage);
        Assert.Equal(new[] { "Machine Lubrication", "Mold Cleaning" }, result.Items.Select(i => i.ChecklistName));
        Assert.Equal(new[] { "Oil level checked", "Grease points lubricated" }, result.Items[0].Items.Select(i => i.ItemLabel));
        Assert.All(result.Items, i => Assert.False(string.IsNullOrEmpty(i.RowVersion)));
    }

    [Fact]
    public async Task GetAll_PagesTenAtATime()
    {
        var s = Create();
        for (var i = 1; i <= 22; i++)
        {
            await s.Service.CreateAsync(NewChecklist(name: $"Bulk {i:00}"), 1, null, CancellationToken.None);
        }

        var names = new List<string>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await s.Service.GetAllAsync(new MaintenanceChecklistListQuery { PageNumber = page, PageSize = 10, Search = "Bulk" }, CancellationToken.None);
            Assert.Equal(22, result.TotalCount);
            Assert.Equal(page < 3 ? 10 : 2, result.Items.Count);
            names.AddRange(result.Items.Select(i => i.ChecklistName));
        }

        Assert.Equal(Enumerable.Range(1, 22).Select(i => $"Bulk {i:00}"), names); // 1-10, 11-20, 21-22, no overlap
    }

    [Fact]
    public async Task GetAll_FiltersByStatus_AppliesTo_AndSearchesCodeAndName()
    {
        var s = Create();
        async Task<List<string>> Codes(MaintenanceChecklistListQuery q) => (await s.Service.GetAllAsync(q, CancellationToken.None)).Items.Select(i => i.ChecklistCode).ToList();

        Assert.Equal(new[] { "CHK-0001", "CHK-0002" }, await Codes(new MaintenanceChecklistListQuery { IsActive = true }));
        Assert.Equal(new[] { "CHK-0003" }, await Codes(new MaintenanceChecklistListQuery { IsActive = false }));
        Assert.Equal(new[] { "CHK-0002" }, await Codes(new MaintenanceChecklistListQuery { AppliesTo = "Mold" }));
        Assert.Equal(new[] { "CHK-0001" }, await Codes(new MaintenanceChecklistListQuery { Search = "lubric" }));
        Assert.Equal(new[] { "CHK-0003" }, await Codes(new MaintenanceChecklistListQuery { Search = "chk-0003" }));
        Assert.Empty(await Codes(new MaintenanceChecklistListQuery { AppliesTo = "Both" }));
    }

    [Fact]
    public async Task GetById_ReturnsEveryFieldAndItemsInOrder_AndThrowsNotFoundForUnknown()
    {
        var s = Create();

        var dto = await s.Service.GetByIdAsync(1, CancellationToken.None);

        Assert.Equal("CHK-0001", dto.ChecklistCode);
        Assert.Equal("Machine Lubrication", dto.ChecklistName);
        Assert.Equal("Machine", dto.AppliesTo);
        Assert.True(dto.IsActive);
        Assert.Equal(new[] { 1, 2 }, dto.Items.Select(i => i.SortOrder));
        Assert.Equal(1, dto.CreatedBy);
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.GetByIdAsync(999, CancellationToken.None));
    }

    // ================================================================ create

    [Fact]
    public async Task Create_IssuesTheCode_Normalizes_DropsBlankItems_NumbersThem_AndAudits()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(
            NewChecklist(name: "  Electrical Checkup ", appliesTo: " mold ", items: Items("  Wiring ", "", null, "   ", "Earth tested")),
            1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("CHK-0004", dto.ChecklistCode);
        Assert.Equal("Electrical Checkup", dto.ChecklistName);
        Assert.Equal("Mold", dto.AppliesTo); // normalized onto the CK value
        Assert.True(dto.IsActive);
        Assert.Equal(1, dto.CreatedBy);
        Assert.Equal(FixedClock.Now, dto.CreatedAt);
        Assert.Null(dto.UpdatedBy);
        Assert.Equal(new[] { "Wiring", "Earth tested" }, dto.Items.Select(i => i.ItemLabel));
        Assert.Equal(new[] { 1, 2 }, dto.Items.Select(i => i.SortOrder));
        Assert.All(s.Checklists.Stored(dto.ChecklistId).Items, i => Assert.Equal((FixedClock.Now, (int?)1), (i.CreatedAt, i.CreatedBy)));

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("ChecklistCreated", entry.Action);
        Assert.Equal("Maintenance Checklist", entry.Module);
        Assert.Equal("MaintenanceChecklist", entry.EntityName);
        Assert.Equal(dto.ChecklistId, entry.EntityId);
        Assert.Equal("CHK-0004", entry.RecordRef);
        Assert.Equal(1, entry.UserId);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Equal("10.0.0.5", entry.IpAddress);
        Assert.Contains("with 2 items", entry.Description);
    }

    [Theory]
    [InlineData("Machine")]
    [InlineData("Mold")]
    public async Task Create_AcceptsEveryAppliesToValueTheCheckConstraintAllows(string appliesTo)
    {
        var dto = await Create().Service.CreateAsync(NewChecklist(appliesTo: appliesTo), 1, null, CancellationToken.None);

        Assert.Equal(appliesTo, dto.AppliesTo);
    }

    [Fact]
    public async Task Create_RequiresNameAppliesToAndAnItem_RejectsBoth_AndOverlongValues_WritingNothing()
    {
        var s = Create();

        var missing = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.CreateAsync(NewChecklist(name: " ", appliesTo: null, items: Items(" ", "")), 1, null, CancellationToken.None));
        Assert.Contains("ChecklistName is required.", missing.Errors);
        Assert.Contains("AppliesTo is required.", missing.Errors);
        Assert.Contains("Please add at least one checklist item.", missing.Errors);

        var noItems = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.CreateAsync(new CreateMaintenanceChecklistRequest { ChecklistName = "X", AppliesTo = "Machine", Items = null }, 1, null, CancellationToken.None));
        Assert.Contains("Please add at least one checklist item.", noItems.Errors);

        var invalid = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.CreateAsync(NewChecklist(name: new string('n', 151), appliesTo: "Both", items: Items("ok", new string('i', 201))), 1, null, CancellationToken.None));
        Assert.Contains("ChecklistName must be at most 150 characters.", invalid.Errors);
        Assert.Contains("AppliesTo must be one of: Machine, Mold.", invalid.Errors); // no "Both" for checklists
        Assert.Contains("Checklist item 2 must be at most 200 characters.", invalid.Errors);

        Assert.Equal(0, s.Checklists.AddCalls);
        Assert.Empty(s.Audit.Entries);

        var atLimit = await s.Service.CreateAsync(NewChecklist(name: new string('n', 150), items: Items(new string('i', 200))), 1, null, CancellationToken.None);
        Assert.Equal(150, atLimit.ChecklistName.Length);
        Assert.Equal(200, atLimit.Items[0].ItemLabel.Length);
    }

    [Theory]
    [InlineData("Machine Lubrication")]
    [InlineData("  mold cleaning ")]
    public async Task Create_RefusesADuplicateActiveName_CaseInsensitively_WithConflict(string name)
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateAsync(NewChecklist(name: name), 1, null, CancellationToken.None));

        Assert.Contains("already exists", ex.Message);
        Assert.Equal(0, s.Checklists.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_AllowsTheNameOfAnInactiveChecklist()
    {
        var dto = await Create().Service.CreateAsync(NewChecklist(name: "Old Checklist"), 1, null, CancellationToken.None);

        Assert.True(dto.IsActive);
    }

    // ================================================================ update

    [Fact]
    public async Task Update_ChangesHeaderAndReplacesItems_OnlyThoseColumns_AndAuditsTheChanges()
    {
        var s = Create();
        var before = s.Checklists.Stored(1);
        var (code, active, createdBy, createdAt, oldVersion) = (before.ChecklistCode, before.IsActive, before.CreatedBy, before.CreatedAt, Convert.ToBase64String(before.RowVersion));

        var dto = await s.Service.UpdateAsync(1,
            Edit(s.Checklists, 1, name: "Machine Lube", appliesTo: "Mold", items: Items("Grease points lubricated", "", "Oil level checked", "Filter replaced")),
            1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("Machine Lube", dto.ChecklistName);
        Assert.Equal("Mold", dto.AppliesTo);
        Assert.Equal(new[] { "Grease points lubricated", "Oil level checked", "Filter replaced" }, dto.Items.Select(i => i.ItemLabel));
        Assert.Equal(new[] { 1, 2, 3 }, dto.Items.Select(i => i.SortOrder));
        Assert.True(s.Checklists.LastReplaceItems);

        var after = s.Checklists.Stored(1);
        Assert.Equal((code, active, createdBy, createdAt), (after.ChecklistCode, after.IsActive, after.CreatedBy, after.CreatedAt));
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(FixedClock.Now, after.UpdatedAt);
        Assert.Equal(3, after.Items.Count);
        Assert.NotEqual(oldVersion, dto.RowVersion);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("ChecklistUpdated", entry.Action);
        Assert.Equal("CHK-0001", entry.RecordRef);
        Assert.Contains("Changed: checklist_name, applies_to, machine, items.", entry.Description);
        Assert.Contains(entry.Details, d => d is { FieldName: "machine", OldValue: "MAC-0001", NewValue: null });
        Assert.Contains(entry.Details, d => d is { FieldName: "checklist_name", OldValue: "Machine Lubrication", NewValue: "Machine Lube" });
        Assert.Contains(entry.Details, d => d is { FieldName: "applies_to", OldValue: "Machine", NewValue: "Mold" });
        Assert.Contains(entry.Details, d => d is { FieldName: "items", OldValue: "2 items", NewValue: "3 items" });
    }

    [Fact]
    public async Task Update_WithUnchangedItems_DoesNotRewriteThem_SoItemIdsAreKept()
    {
        var s = Create();
        var itemIds = s.Checklists.Stored(1).Items.Select(i => i.ChecklistItemId).ToList();

        var dto = await s.Service.UpdateAsync(1, Edit(s.Checklists, 1, name: "Machine Lubrication v2"), 1, null, CancellationToken.None);

        Assert.False(s.Checklists.LastReplaceItems);
        Assert.Equal(itemIds, dto.Items.Select(i => i.ChecklistItemId));
        Assert.Equal(itemIds, s.Checklists.Stored(1).Items.Select(i => i.ChecklistItemId));
        Assert.DoesNotContain(Assert.Single(s.Audit.Entries).Details, d => d.FieldName == "items");
    }

    [Fact]
    public async Task Update_ReorderingItems_CountsAsAChange()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Checklists, 1, items: Items("Grease points lubricated", "Oil level checked")), 1, null, CancellationToken.None);

        Assert.True(s.Checklists.LastReplaceItems);
        Assert.Equal("Grease points lubricated", s.Checklists.Stored(1).Items.Single(i => i.SortOrder == 1).ItemLabel);
        Assert.Contains(Assert.Single(s.Audit.Entries).Details, d => d is { FieldName: "items", OldValue: "2 items", NewValue: "2 items" });
    }

    [Fact]
    public async Task Update_WithNoChange_StillSucceeds_AndSaysNothingChanged()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Checklists, 1), 1, null, CancellationToken.None);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Contains("No field values changed.", entry.Description);
        Assert.Empty(entry.Details);
    }

    [Fact]
    public async Task Update_AcceptsItsOwnName_ButRefusesAnotherActiveChecklistsName()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Checklists, 1, name: "MACHINE LUBRICATION"), 1, null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(1, Edit(s.Checklists, 1, name: "mold cleaning"), 1, null, CancellationToken.None));
        Assert.Contains("already exists", ex.Message);
        Assert.Equal("MACHINE LUBRICATION", s.Checklists.Stored(1).ChecklistName);
        Assert.Single(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_UnknownIsNotFound_AndFieldsItemsAndRowVersionAreValidated_WritingNothing()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.UpdateAsync(999, Edit(s.Checklists, 1), 1, null, CancellationToken.None));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(1,
            new UpdateMaintenanceChecklistRequest { ChecklistName = "", AppliesTo = "Tool", Items = Items(""), RowVersion = "" }, 1, null, CancellationToken.None));
        Assert.Contains("ChecklistName is required.", ex.Errors);
        Assert.Contains("AppliesTo must be one of: Machine, Mold.", ex.Errors);
        Assert.Contains("Please add at least one checklist item.", ex.Errors);
        Assert.Contains("RowVersion is required.", ex.Errors);

        var bad = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(1, Edit(s.Checklists, 1, rowVersion: "not base64 !!"), 1, null, CancellationToken.None));
        Assert.Contains("RowVersion is not valid.", bad.Errors);

        Assert.Equal(0, s.Checklists.UpdateCalls);
        Assert.Equal(2, s.Checklists.Stored(1).Items.Count);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_WithAStaleRowVersion_ThrowsConflict_AndDoesNotOverwriteHeaderOrItems_OrAudit()
    {
        var s = Create();
        var stale = Edit(s.Checklists, 1, name: "Stale edit", items: Items("Only one"));
        s.Checklists.SimulateConcurrentModification(1);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(1, stale, 1, null, CancellationToken.None));

        Assert.Equal("The checklist was modified by another user. Refresh the checklist and try again.", ex.Message);
        Assert.Equal("Machine Lubrication", s.Checklists.Stored(1).ChecklistName);
        Assert.Equal(2, s.Checklists.Stored(1).Items.Count);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ deactivate

    [Fact]
    public async Task Deactivate_SetsInactive_KeepsTheRowAndItems_ChangesNothingElse_AndAudits()
    {
        var s = Create();
        var before = s.Checklists.Stored(2);
        var snapshot = (before.ChecklistCode, before.ChecklistName, before.AppliesTo, before.CreatedBy, before.CreatedAt, before.Items.Count);

        var dto = await s.Service.DeactivateAsync(2, 1, "10.0.0.5", CancellationToken.None);

        Assert.False(dto.IsActive);
        var after = s.Checklists.Stored(2);
        Assert.False(after.IsActive);
        Assert.Equal(snapshot, (after.ChecklistCode, after.ChecklistName, after.AppliesTo, after.CreatedBy, after.CreatedAt, after.Items.Count));
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(3, s.Checklists.Count);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("ChecklistDeactivated", entry.Action);
        Assert.Equal("CHK-0002", entry.RecordRef);
        Assert.Equal("10.0.0.5", entry.IpAddress);
        Assert.Contains(entry.Details, d => d is { FieldName: "is_active", OldValue: "True", NewValue: "False" });
    }

    [Fact]
    public async Task Deactivate_UnknownIsNotFound_AlreadyInactiveIsAConflict_WritingNothing()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.DeactivateAsync(999, 1, null, CancellationToken.None));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(3, 1, null, CancellationToken.None));

        Assert.Equal("The checklist is already inactive.", ex.Message);
        Assert.Equal(0, s.Checklists.DeactivateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Deactivate_WhenTheRowChangesBetweenTheReadAndTheSave_ThrowsConflict_AndStaysActive()
    {
        var s = Create();
        s.Checklists.BeforeWrite = s.Checklists.SimulateConcurrentModification;

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(1, 1, null, CancellationToken.None));

        Assert.True(s.Checklists.Stored(1).IsActive);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ audit failure behaviour

    [Fact]
    public async Task WhenTheActingUsersNameCannotBeResolved_TheOperationStillSucceeds_AuditedAsUnknown()
    {
        var s = Create();
        s.Users.ThrowOnGetByIdCall = 1;

        var dto = await s.Service.CreateAsync(NewChecklist(), 1, null, CancellationToken.None);

        Assert.Equal("CHK-0004", dto.ChecklistCode);
        Assert.Equal(new[] { "ChecklistCreated", "MachinePmScheduled" }, s.Audit.Entries.Select(e => e.Action)); // Machine checklist -> first occurrence
        Assert.Equal("Unknown", s.Audit.Entries[0].UserName); // only the first name lookup was made to fail
        Assert.Equal("Sakthi", s.Audit.Entries[1].UserName);
    }

    [Fact]
    public async Task WhenWritingTheAuditRowFails_EveryOperationStillSucceeds()
    {
        var audit = new AuditLogService(new ThrowingAuditRepository(), new FixedClock(), NullLogger<AuditLogService>.Instance);
        var s = Create(auditOverride: audit);

        var created = await s.Service.CreateAsync(NewChecklist(), 1, null, CancellationToken.None);
        var updated = await s.Service.UpdateAsync(created.ChecklistId, Edit(s.Checklists, created.ChecklistId, name: "Hydraulics"), 1, null, CancellationToken.None);
        var deactivated = await s.Service.DeactivateAsync(created.ChecklistId, 1, null, CancellationToken.None);

        Assert.Equal("Hydraulics", updated.ChecklistName);
        Assert.False(deactivated.IsActive);
        Assert.False(s.Checklists.Stored(created.ChecklistId).IsActive);
    }

    [Fact]
    public async Task EveryAuditEntry_FitsTheAuditLogColumns()
    {
        // audit.audit_log: action VARCHAR(30), module NVARCHAR(50), entity_name VARCHAR(50), record_ref NVARCHAR(50).
        // "MaintenanceChecklistDeactivated" (31) was lost on the real database - found by the rolled-back verification.
        var s = Create();

        var created = await s.Service.CreateAsync(NewChecklist(), 1, null, CancellationToken.None);
        await s.Service.UpdateAsync(created.ChecklistId, Edit(s.Checklists, created.ChecklistId, name: "Renamed"), 1, null, CancellationToken.None);
        await s.Service.DeactivateAsync(created.ChecklistId, 1, null, CancellationToken.None);

        Assert.Equal(new[] { "ChecklistCreated", "MachinePmScheduled", "ChecklistUpdated", "ChecklistDeactivated" }, s.Audit.Entries.Select(e => e.Action));
        Assert.All(s.Audit.Entries, e =>
        {
            Assert.True(e.Action.Length <= 30, e.Action);
            Assert.True(e.Module.Length <= 50, e.Module);
            Assert.True(e.EntityName!.Length <= 50, e.EntityName);
            Assert.True(e.RecordRef!.Length <= 50, e.RecordRef);
        });
    }

    private sealed class ThrowingAuditRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit persistence failure.");
    }
}
