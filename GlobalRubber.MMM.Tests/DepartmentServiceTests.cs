using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The real DepartmentService against in-memory fakes. Acting user 1 "Sakthi". Departments: 1 Injection Moulding
/// (active, with remarks), 2 Mixing (active), 3 Old Dept (inactive).
/// </summary>
public class DepartmentServiceTests
{
    private sealed record Sut(
        DepartmentService Service, InMemoryDepartmentRepository Departments, RecordingAuditLog Audit, InMemoryUserRepository Users);

    private static Sut Create(IAuditLogService? auditOverride = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var audit = new RecordingAuditLog();
        var service = new DepartmentService(departments, users, new FixedClock(), auditOverride ?? audit, NullLogger<DepartmentService>.Instance);
        return new Sut(service, departments, audit, users);
    }

    private static string Rv(InMemoryDepartmentRepository repo, int id) => Convert.ToBase64String(repo.Stored(id).RowVersion);

    private static UpdateDepartmentRequest Edit(InMemoryDepartmentRepository repo, int id, string? name = null, string? remarks = "keep", string? rowVersion = null) =>
        new()
        {
            DepartmentName = name ?? repo.Stored(id).DepartmentName,
            Remarks = remarks == "keep" ? repo.Stored(id).Remarks : remarks,
            RowVersion = rowVersion ?? Rv(repo, id),
        };

    // ================================================================ list / get

    [Fact]
    public async Task GetAll_ReturnsAPagedResult_OrderedByName_WithTheRowVersion()
    {
        var s = Create();

        var result = await s.Service.GetAllAsync(new DepartmentListQuery { PageNumber = 1, PageSize = 2 }, CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Equal(new[] { "Injection Moulding", "Mixing" }, result.Items.Select(i => i.DepartmentName));
        Assert.All(result.Items, i => Assert.False(string.IsNullOrEmpty(i.RowVersion)));
    }

    [Fact]
    public async Task GetAll_FiltersByStatus_AndSearchesCodeAndName()
    {
        var s = Create();

        var active = await s.Service.GetAllAsync(new DepartmentListQuery { IsActive = true }, CancellationToken.None);
        var inactive = await s.Service.GetAllAsync(new DepartmentListQuery { IsActive = false }, CancellationToken.None);
        var byName = await s.Service.GetAllAsync(new DepartmentListQuery { Search = "mix" }, CancellationToken.None);
        var byCode = await s.Service.GetAllAsync(new DepartmentListQuery { Search = "dep-0003" }, CancellationToken.None);

        Assert.Equal(2, active.TotalCount);
        Assert.Equal("Old Dept", Assert.Single(inactive.Items).DepartmentName);
        Assert.Equal("Mixing", Assert.Single(byName.Items).DepartmentName);
        Assert.Equal("Old Dept", Assert.Single(byCode.Items).DepartmentName);
    }

    [Fact]
    public async Task GetById_ReturnsTheDepartment()
    {
        var dto = await Create().Service.GetByIdAsync(1, CancellationToken.None);

        Assert.Equal("DEP-0001", dto.DepartmentCode);
        Assert.Equal("Injection Moulding", dto.DepartmentName);
        Assert.Equal("Shop floor 1", dto.Remarks);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public async Task GetById_ThrowsNotFound_ForAnUnknownDepartment()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Create().Service.GetByIdAsync(999, CancellationToken.None));
    }

    // ================================================================ create

    [Fact]
    public async Task Create_IssuesTheCode_SetsActive_AndCreatedBy_AndAudits()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(new CreateDepartmentRequest { DepartmentName = "  Quality Control  ", Remarks = "  QC lab " }, 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("DEP-0004", dto.DepartmentCode);
        Assert.Equal("Quality Control", dto.DepartmentName);
        Assert.Equal("QC lab", dto.Remarks);
        Assert.True(dto.IsActive);
        Assert.False(string.IsNullOrEmpty(dto.RowVersion));

        var stored = s.Departments.Stored(dto.DepartmentId);
        Assert.Equal(1, stored.CreatedBy);
        Assert.Equal(FixedClock.Now, stored.CreatedAt);
        Assert.Null(stored.UpdatedBy);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("DepartmentCreated", entry.Action);
        Assert.Equal("Department", entry.Module);
        Assert.Equal("Department", entry.EntityName);
        Assert.Equal(dto.DepartmentId, entry.EntityId);
        Assert.Equal("DEP-0004", entry.RecordRef);
        Assert.Equal(1, entry.UserId);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Equal("10.0.0.5", entry.IpAddress);
        Assert.Contains("Quality Control", entry.Description);
    }

    [Fact]
    public async Task Create_TurnsBlankRemarksIntoNull()
    {
        var dto = await Create().Service.CreateAsync(new CreateDepartmentRequest { DepartmentName = "Stores", Remarks = "   " }, 1, null, CancellationToken.None);

        Assert.Null(dto.Remarks);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_RejectsAMissingName_WithValidation_AndWritesNothing(string name)
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.CreateAsync(new CreateDepartmentRequest { DepartmentName = name }, 1, null, CancellationToken.None));

        Assert.Contains("DepartmentName is required.", ex.Errors);
        Assert.Equal(0, s.Departments.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_RejectsATooLongNameAndRemarks_MatchingTheColumnLengths()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.CreateAsync(new CreateDepartmentRequest { DepartmentName = new string('x', 101), Remarks = new string('r', 501) }, 1, null, CancellationToken.None));

        Assert.Contains("DepartmentName must be at most 100 characters.", ex.Errors);
        Assert.Contains("Remarks must be at most 500 characters.", ex.Errors);

        // exactly at the limit is fine
        var ok = await s.Service.CreateAsync(new CreateDepartmentRequest { DepartmentName = new string('x', 100), Remarks = new string('r', 500) }, 1, null, CancellationToken.None);
        Assert.Equal(100, ok.DepartmentName.Length);
    }

    [Theory]
    [InlineData("Mixing")]
    [InlineData("  mixing ")]
    [InlineData("MIXING")]
    public async Task Create_RefusesADuplicateActiveName_CaseInsensitively_WithConflict(string name)
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            s.Service.CreateAsync(new CreateDepartmentRequest { DepartmentName = name }, 1, null, CancellationToken.None));

        Assert.Contains("already exists", ex.Message);
        Assert.Equal(0, s.Departments.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_AllowsTheNameOfAnInactiveDepartment()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(new CreateDepartmentRequest { DepartmentName = "Old Dept" }, 1, null, CancellationToken.None);

        Assert.True(dto.IsActive);
        Assert.Equal(4, s.Departments.Count);
    }

    // ================================================================ update

    [Fact]
    public async Task Update_ChangesNameAndRemarks_OnlyThoseColumns_AndSetsUpdatedBy()
    {
        var s = Create();
        var before = s.Departments.Stored(1);
        var (code, active, createdBy, createdAt, oldVersion) = (before.DepartmentCode, before.IsActive, before.CreatedBy, before.CreatedAt, Rv(s.Departments, 1));

        var dto = await s.Service.UpdateAsync(1, Edit(s.Departments, 1, name: "Injection Moulding Dept", remarks: "Bay 1-4"), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("Injection Moulding Dept", dto.DepartmentName);
        Assert.Equal("Bay 1-4", dto.Remarks);
        var after = s.Departments.Stored(1);
        Assert.Equal(code, after.DepartmentCode);
        Assert.Equal(active, after.IsActive);
        Assert.Equal(createdBy, after.CreatedBy);
        Assert.Equal(createdAt, after.CreatedAt);
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(FixedClock.Now, after.UpdatedAt);
        Assert.NotEqual(oldVersion, dto.RowVersion); // a fresh version comes back so the form can save again
    }

    [Fact]
    public async Task Update_AuditsFieldLevelChanges_NamesAndOldNewValues()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Departments, 1, name: "Moulding", remarks: null), 1, "10.0.0.5", CancellationToken.None);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("DepartmentUpdated", entry.Action);
        Assert.Equal("DEP-0001", entry.RecordRef);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Contains("department_name, remarks", entry.Description);
        Assert.Equal(2, entry.Details.Count);
        Assert.Contains(entry.Details, d => d is { FieldName: "department_name", OldValue: "Injection Moulding", NewValue: "Moulding" });
        Assert.Contains(entry.Details, d => d is { FieldName: "remarks", OldValue: "Shop floor 1", NewValue: null });
    }

    [Fact]
    public async Task Update_WithNoChange_StillSucceeds_AndSaysNothingChanged()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Departments, 1), 1, null, CancellationToken.None);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Contains("No field values changed.", entry.Description);
        Assert.Empty(entry.Details);
    }

    [Fact]
    public async Task Update_AcceptsItsOwnName_ButRefusesAnotherActiveDepartmentsName()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Departments, 1, name: "injection moulding"), 1, null, CancellationToken.None); // own name, re-cased: fine

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            s.Service.UpdateAsync(1, Edit(s.Departments, 1, name: "Mixing"), 1, null, CancellationToken.None));
        Assert.Contains("already exists", ex.Message);
        Assert.Equal("injection moulding", s.Departments.Stored(1).DepartmentName);
    }

    [Fact]
    public async Task Update_ThrowsNotFound_ForAnUnknownDepartment_WithoutWritingOrAuditing()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            s.Service.UpdateAsync(999, new UpdateDepartmentRequest { DepartmentName = "X", RowVersion = "AQ==" }, 1, null, CancellationToken.None));

        Assert.Equal(0, s.Departments.UpdateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_ValidatesName_Remarks_AndTheRowVersion()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.UpdateAsync(1, new UpdateDepartmentRequest { DepartmentName = " ", Remarks = new string('r', 501), RowVersion = "" }, 1, null, CancellationToken.None));
        Assert.Contains("DepartmentName is required.", ex.Errors);
        Assert.Contains("Remarks must be at most 500 characters.", ex.Errors);
        Assert.Contains("RowVersion is required.", ex.Errors);

        var bad = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.UpdateAsync(1, new UpdateDepartmentRequest { DepartmentName = "Ok", RowVersion = "not base64 !!" }, 1, null, CancellationToken.None));
        Assert.Contains("RowVersion is not valid.", bad.Errors);

        Assert.Equal(0, s.Departments.UpdateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_WithAStaleRowVersion_ThrowsConflict_AndDoesNotOverwriteOrAudit()
    {
        var s = Create();
        var staleRequest = Edit(s.Departments, 1, name: "Stale edit"); // the form was loaded with version 1...
        s.Departments.SimulateConcurrentModification(1);               // ...then someone else saved

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(1, staleRequest, 1, null, CancellationToken.None));

        Assert.Contains("modified by another user", ex.Message);
        Assert.Equal("Injection Moulding", s.Departments.Stored(1).DepartmentName);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_WhenTheRowChangesBetweenTheReadAndTheSave_ThrowsConflict()
    {
        var s = Create();
        s.Departments.BeforeWrite = s.Departments.SimulateConcurrentModification;

        await Assert.ThrowsAsync<ConflictException>(() =>
            s.Service.UpdateAsync(1, Edit(s.Departments, 1, name: "Race"), 1, null, CancellationToken.None));

        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ deactivate

    [Fact]
    public async Task Deactivate_SetsInactive_KeepsTheRow_ChangesNothingElse_AndAudits()
    {
        var s = Create();
        var before = s.Departments.Stored(2);
        var (id, code, name, remarks, createdBy, createdAt) = (before.DepartmentId, before.DepartmentCode, before.DepartmentName, before.Remarks, before.CreatedBy, before.CreatedAt);

        var dto = await s.Service.DeactivateAsync(2, 1, "10.0.0.5", CancellationToken.None);

        Assert.False(dto.IsActive);
        var after = s.Departments.Stored(2); // Stored() throws if the row had been removed
        Assert.False(after.IsActive);
        Assert.Equal((id, code, name, remarks, createdBy, createdAt), (after.DepartmentId, after.DepartmentCode, after.DepartmentName, after.Remarks, after.CreatedBy, after.CreatedAt));
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(FixedClock.Now, after.UpdatedAt);
        Assert.Equal(3, s.Departments.Count);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("DepartmentDeactivated", entry.Action);
        Assert.Equal("DEP-0002", entry.RecordRef);
        Assert.Equal(2, entry.EntityId);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Equal("10.0.0.5", entry.IpAddress);
    }

    [Fact]
    public async Task Deactivate_ThrowsNotFound_ForAnUnknownDepartment()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.DeactivateAsync(999, 1, null, CancellationToken.None));

        Assert.Equal(0, s.Departments.DeactivateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Deactivate_RefusesAnAlreadyInactiveDepartment_WithConflict_WritingAndAuditingNothing()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(3, 1, null, CancellationToken.None));

        Assert.Contains("already inactive", ex.Message);
        Assert.Equal(0, s.Departments.DeactivateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Deactivate_WhenTheRowChangesBetweenTheReadAndTheSave_ThrowsConflict_AndStaysActive()
    {
        var s = Create();
        s.Departments.BeforeWrite = s.Departments.SimulateConcurrentModification;

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(2, 1, null, CancellationToken.None));

        Assert.True(s.Departments.Stored(2).IsActive);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ audit failure behaviour

    [Fact]
    public async Task WhenTheActingUsersNameCannotBeResolved_TheOperationStillSucceeds_AuditedAsUnknown()
    {
        var s = Create();
        s.Users.ThrowOnGetByIdCall = 1;

        var dto = await s.Service.CreateAsync(new CreateDepartmentRequest { DepartmentName = "Stores" }, 1, null, CancellationToken.None);

        Assert.Equal("DEP-0004", dto.DepartmentCode);
        Assert.Equal("Unknown", Assert.Single(s.Audit.Entries).UserName);
    }

    [Fact]
    public async Task WhenWritingTheAuditRowFails_TheOperationStillSucceeds()
    {
        // The real AuditLogService (the one production wires in) over a repository that always fails.
        var audit = new AuditLogService(new ThrowingAuditRepository(), new FixedClock(), NullLogger<AuditLogService>.Instance);
        var s = Create(auditOverride: audit);

        var created = await s.Service.CreateAsync(new CreateDepartmentRequest { DepartmentName = "Stores" }, 1, null, CancellationToken.None);
        var updated = await s.Service.UpdateAsync(created.DepartmentId, Edit(s.Departments, created.DepartmentId, name: "Stores 2"), 1, null, CancellationToken.None);
        var deactivated = await s.Service.DeactivateAsync(created.DepartmentId, 1, null, CancellationToken.None);

        Assert.Equal("Stores 2", updated.DepartmentName);
        Assert.False(deactivated.IsActive);
        Assert.False(s.Departments.Stored(created.DepartmentId).IsActive);
    }

    private sealed class ThrowingAuditRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit persistence failure.");
    }
}
