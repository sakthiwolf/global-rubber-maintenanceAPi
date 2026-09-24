using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The real MaintenanceTypeService against in-memory fakes. Acting user 1 "Sakthi". Types: 1 Oil &amp; Lubrication
/// (Machine), 2 General Inspection (Both) - active; 3 Old Type (Mold) - inactive.
/// </summary>
public class MaintenanceTypeServiceTests
{
    private sealed record Sut(MaintenanceTypeService Service, InMemoryMaintenanceTypeRepository Types, RecordingAuditLog Audit, InMemoryUserRepository Users);

    private static Sut Create(IAuditLogService? auditOverride = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var types = new InMemoryMaintenanceTypeRepository(MaintenanceTypeTestData.MaintenanceTypes());
        var audit = new RecordingAuditLog();
        var service = new MaintenanceTypeService(types, users, new FixedClock(), auditOverride ?? audit, NullLogger<MaintenanceTypeService>.Instance);
        return new Sut(service, types, audit, users);
    }

    private static CreateMaintenanceTypeRequest NewType(string name = "Hydraulic System Service", string? appliesTo = "Machine") =>
        new() { MaintenanceTypeName = name, AppliesTo = appliesTo };

    private static UpdateMaintenanceTypeRequest Edit(InMemoryMaintenanceTypeRepository repo, int id, string? name = null, string? appliesTo = null, string? rowVersion = null) =>
        new()
        {
            MaintenanceTypeName = name ?? repo.Stored(id).MaintenanceTypeName,
            AppliesTo = appliesTo ?? repo.Stored(id).AppliesTo,
            RowVersion = rowVersion ?? Convert.ToBase64String(repo.Stored(id).RowVersion),
        };

    // ================================================================ list / get

    [Fact]
    public async Task GetAll_ReturnsAPagedResult_OrderedByName()
    {
        var result = await Create().Service.GetAllAsync(new MaintenanceTypeListQuery { PageNumber = 1, PageSize = 2 }, CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(new[] { "General Inspection", "Oil & Lubrication" }, result.Items.Select(i => i.MaintenanceTypeName));
        Assert.All(result.Items, i => Assert.False(string.IsNullOrEmpty(i.RowVersion)));
    }

    [Fact]
    public async Task GetAll_FiltersByStatus_AppliesTo_AndSearchesCodeAndName()
    {
        var s = Create();
        async Task<List<string>> Codes(MaintenanceTypeListQuery q) => (await s.Service.GetAllAsync(q, CancellationToken.None)).Items.Select(i => i.MaintenanceTypeCode).ToList();

        Assert.Equal(new[] { "MT-0002", "MT-0001" }, await Codes(new MaintenanceTypeListQuery { IsActive = true }));
        Assert.Equal(new[] { "MT-0003" }, await Codes(new MaintenanceTypeListQuery { IsActive = false }));
        Assert.Equal(new[] { "MT-0002" }, await Codes(new MaintenanceTypeListQuery { AppliesTo = "Both" }));
        Assert.Equal(new[] { "MT-0001" }, await Codes(new MaintenanceTypeListQuery { Search = "lubric" }));
        Assert.Equal(new[] { "MT-0003" }, await Codes(new MaintenanceTypeListQuery { Search = "mt-0003" }));
        Assert.Empty(await Codes(new MaintenanceTypeListQuery { AppliesTo = "Tool" }));
    }

    [Fact]
    public async Task GetById_ReturnsEveryField_AndThrowsNotFoundForUnknown()
    {
        var s = Create();

        var dto = await s.Service.GetByIdAsync(1, CancellationToken.None);

        Assert.Equal("MT-0001", dto.MaintenanceTypeCode);
        Assert.Equal("Oil & Lubrication", dto.MaintenanceTypeName);
        Assert.Equal("Machine", dto.AppliesTo);
        Assert.True(dto.IsActive);
        Assert.Equal(1, dto.CreatedBy);
        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.GetByIdAsync(999, CancellationToken.None));
    }

    // ================================================================ create

    [Fact]
    public async Task Create_IssuesTheCode_NormalizesAppliesTo_SetsActiveAndCreatedBy_AndAudits()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(NewType(name: "  Electrical Checkup ", appliesTo: " machine "), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("MT-0004", dto.MaintenanceTypeCode);
        Assert.Equal("Electrical Checkup", dto.MaintenanceTypeName);
        Assert.Equal("Machine", dto.AppliesTo); // normalized onto the CK value
        Assert.True(dto.IsActive);
        Assert.Equal(1, dto.CreatedBy);
        Assert.Equal(FixedClock.Now, dto.CreatedAt);
        Assert.Null(dto.UpdatedBy);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("MaintenanceTypeCreated", entry.Action);
        Assert.Equal("Maintenance Type", entry.Module);
        Assert.Equal("MaintenanceType", entry.EntityName);
        Assert.Equal(dto.MaintenanceTypeId, entry.EntityId);
        Assert.Equal("MT-0004", entry.RecordRef);
        Assert.Equal(1, entry.UserId);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Equal("10.0.0.5", entry.IpAddress);
    }

    [Theory]
    [InlineData("Machine")]
    [InlineData("Mold")]
    [InlineData("Both")]
    public async Task Create_AcceptsEveryAppliesToValueTheCheckConstraintAllows(string appliesTo)
    {
        var dto = await Create().Service.CreateAsync(NewType(appliesTo: appliesTo), 1, null, CancellationToken.None);

        Assert.Equal(appliesTo, dto.AppliesTo);
    }

    [Fact]
    public async Task Create_RequiresNameAndAppliesTo_RejectsUnknownAppliesTo_AndOverlongNames_WritingNothing()
    {
        var s = Create();

        var missing = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(NewType(name: " ", appliesTo: null), 1, null, CancellationToken.None));
        Assert.Contains("MaintenanceTypeName is required.", missing.Errors);
        Assert.Contains("AppliesTo is required.", missing.Errors);

        var invalid = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(NewType(name: new string('n', 101), appliesTo: "Tool"), 1, null, CancellationToken.None));
        Assert.Contains("MaintenanceTypeName must be at most 100 characters.", invalid.Errors);
        Assert.Contains("AppliesTo must be one of: Machine, Mold, Both.", invalid.Errors);

        Assert.Equal(0, s.Types.AddCalls);
        Assert.Empty(s.Audit.Entries);

        var atLimit = await s.Service.CreateAsync(NewType(name: new string('n', 100)), 1, null, CancellationToken.None);
        Assert.Equal(100, atLimit.MaintenanceTypeName.Length);
    }

    [Theory]
    [InlineData("Oil & Lubrication")]
    [InlineData("  general inspection ")]
    public async Task Create_RefusesADuplicateActiveName_CaseInsensitively_WithConflict(string name)
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.CreateAsync(NewType(name: name), 1, null, CancellationToken.None));

        Assert.Contains("already exists", ex.Message);
        Assert.Equal(0, s.Types.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_AllowsTheNameOfAnInactiveMaintenanceType()
    {
        var dto = await Create().Service.CreateAsync(NewType(name: "Old Type"), 1, null, CancellationToken.None);

        Assert.True(dto.IsActive);
    }

    // ================================================================ update

    [Fact]
    public async Task Update_ChangesNameAndAppliesTo_OnlyThoseColumns_AndAuditsOldAndNewValues()
    {
        var s = Create();
        var before = s.Types.Stored(1);
        var (code, active, createdBy, createdAt, oldVersion) = (before.MaintenanceTypeCode, before.IsActive, before.CreatedBy, before.CreatedAt, Convert.ToBase64String(before.RowVersion));

        var dto = await s.Service.UpdateAsync(1, Edit(s.Types, 1, name: "Oil & Lube", appliesTo: "Both"), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("Oil & Lube", dto.MaintenanceTypeName);
        Assert.Equal("Both", dto.AppliesTo);
        var after = s.Types.Stored(1);
        Assert.Equal((code, active, createdBy, createdAt), (after.MaintenanceTypeCode, after.IsActive, after.CreatedBy, after.CreatedAt));
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(FixedClock.Now, after.UpdatedAt);
        Assert.NotEqual(oldVersion, dto.RowVersion);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("MaintenanceTypeUpdated", entry.Action);
        Assert.Equal("MT-0001", entry.RecordRef);
        Assert.Contains("Changed: maintenance_type_name, applies_to.", entry.Description);
        Assert.Contains(entry.Details, d => d is { FieldName: "maintenance_type_name", OldValue: "Oil & Lubrication", NewValue: "Oil & Lube" });
        Assert.Contains(entry.Details, d => d is { FieldName: "applies_to", OldValue: "Machine", NewValue: "Both" });
    }

    [Fact]
    public async Task Update_WithNoChange_StillSucceeds_AndSaysNothingChanged()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Types, 1), 1, null, CancellationToken.None);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Contains("No field values changed.", entry.Description);
        Assert.Empty(entry.Details);
    }

    [Fact]
    public async Task Update_AcceptsItsOwnName_ButRefusesAnotherActiveTypesName()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Types, 1, name: "OIL & LUBRICATION"), 1, null, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(1, Edit(s.Types, 1, name: "general inspection"), 1, null, CancellationToken.None));
        Assert.Contains("already exists", ex.Message);
        Assert.Equal("OIL & LUBRICATION", s.Types.Stored(1).MaintenanceTypeName);
        Assert.Single(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_UnknownIsNotFound_AndFieldsAndRowVersionAreValidated_WritingNothing()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.UpdateAsync(999, Edit(s.Types, 1), 1, null, CancellationToken.None));

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(1,
            new UpdateMaintenanceTypeRequest { MaintenanceTypeName = "", AppliesTo = "Tool", RowVersion = "" }, 1, null, CancellationToken.None));
        Assert.Contains("MaintenanceTypeName is required.", ex.Errors);
        Assert.Contains("AppliesTo must be one of: Machine, Mold, Both.", ex.Errors);
        Assert.Contains("RowVersion is required.", ex.Errors);

        var bad = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(1, Edit(s.Types, 1, rowVersion: "not base64 !!"), 1, null, CancellationToken.None));
        Assert.Contains("RowVersion is not valid.", bad.Errors);

        Assert.Equal(0, s.Types.UpdateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_WithAStaleRowVersion_ThrowsConflict_AndDoesNotOverwriteOrAudit()
    {
        var s = Create();
        var stale = Edit(s.Types, 1, name: "Stale edit");
        s.Types.SimulateConcurrentModification(1);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(1, stale, 1, null, CancellationToken.None));

        Assert.Equal("The maintenance type was modified by another user. Refresh the maintenance type and try again.", ex.Message);
        Assert.Equal("Oil & Lubrication", s.Types.Stored(1).MaintenanceTypeName);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ deactivate

    [Fact]
    public async Task Deactivate_SetsInactive_KeepsTheRow_ChangesNothingElse_AndAudits()
    {
        var s = Create();
        var before = s.Types.Stored(2);
        var snapshot = (before.MaintenanceTypeCode, before.MaintenanceTypeName, before.AppliesTo, before.CreatedBy, before.CreatedAt);

        var dto = await s.Service.DeactivateAsync(2, 1, "10.0.0.5", CancellationToken.None);

        Assert.False(dto.IsActive);
        var after = s.Types.Stored(2);
        Assert.False(after.IsActive);
        Assert.Equal(snapshot, (after.MaintenanceTypeCode, after.MaintenanceTypeName, after.AppliesTo, after.CreatedBy, after.CreatedAt));
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(3, s.Types.Count);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("MaintenanceTypeDeactivated", entry.Action);
        Assert.Equal("MT-0002", entry.RecordRef);
        Assert.Equal("10.0.0.5", entry.IpAddress);
    }

    [Fact]
    public async Task Deactivate_UnknownIsNotFound_AlreadyInactiveIsAConflict_WritingNothing()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.DeactivateAsync(999, 1, null, CancellationToken.None));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(3, 1, null, CancellationToken.None));

        Assert.Equal("The maintenance type is already inactive.", ex.Message);
        Assert.Equal(0, s.Types.DeactivateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Deactivate_WhenTheRowChangesBetweenTheReadAndTheSave_ThrowsConflict_AndStaysActive()
    {
        var s = Create();
        s.Types.BeforeWrite = s.Types.SimulateConcurrentModification;

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(1, 1, null, CancellationToken.None));

        Assert.True(s.Types.Stored(1).IsActive);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ audit failure behaviour

    [Fact]
    public async Task WhenTheActingUsersNameCannotBeResolved_TheOperationStillSucceeds_AuditedAsUnknown()
    {
        var s = Create();
        s.Users.ThrowOnGetByIdCall = 1;

        var dto = await s.Service.CreateAsync(NewType(), 1, null, CancellationToken.None);

        Assert.Equal("MT-0004", dto.MaintenanceTypeCode);
        Assert.Equal("Unknown", Assert.Single(s.Audit.Entries).UserName);
    }

    [Fact]
    public async Task WhenWritingTheAuditRowFails_EveryOperationStillSucceeds()
    {
        var audit = new AuditLogService(new ThrowingAuditRepository(), new FixedClock(), NullLogger<AuditLogService>.Instance);
        var s = Create(auditOverride: audit);

        var created = await s.Service.CreateAsync(NewType(), 1, null, CancellationToken.None);
        var updated = await s.Service.UpdateAsync(created.MaintenanceTypeId, Edit(s.Types, created.MaintenanceTypeId, name: "Hydraulics"), 1, null, CancellationToken.None);
        var deactivated = await s.Service.DeactivateAsync(created.MaintenanceTypeId, 1, null, CancellationToken.None);

        Assert.Equal("Hydraulics", updated.MaintenanceTypeName);
        Assert.False(deactivated.IsActive);
        Assert.False(s.Types.Stored(created.MaintenanceTypeId).IsActive);
    }

    private sealed class ThrowingAuditRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit persistence failure.");
    }
}
