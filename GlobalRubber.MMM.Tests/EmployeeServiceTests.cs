using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The real EmployeeService against in-memory fakes. Acting user 1 "Sakthi". Departments: 1 Injection Moulding, 2 Mixing
/// (active), 3 Old Dept (inactive). Employees: 1 Ravi Kumar (dept 1), 2 Karthik Raja (dept 3 - inactive dept), 3 Old Hand (inactive).
/// </summary>
public class EmployeeServiceTests
{
    private sealed record Sut(
        EmployeeService Service, InMemoryEmployeeRepository Employees, InMemoryDepartmentRepository Departments,
        RecordingAuditLog Audit, InMemoryUserRepository Users);

    private static Sut Create(IAuditLogService? auditOverride = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var audit = new RecordingAuditLog();
        var service = new EmployeeService(employees, departments, users, new FixedClock(), auditOverride ?? audit, NullLogger<EmployeeService>.Instance);
        return new Sut(service, employees, departments, audit, users);
    }

    private static CreateEmployeeRequest NewEmployee(
        string name = "Priya Dharshini", string designation = "QC Inspector", int departmentId = 2, string? mobile = "9840011128", string? email = "priya@example.com") =>
        new() { EmployeeName = name, Designation = designation, DepartmentId = departmentId, Mobile = mobile, Email = email };

    private static string Rv(InMemoryEmployeeRepository repo, int id) => Convert.ToBase64String(repo.Stored(id).RowVersion);

    // Update request defaulting to "no change" for the employee, so each test states only what it changes.
    private static UpdateEmployeeRequest Edit(
        InMemoryEmployeeRepository repo, int id, string? name = null, string? designation = null, int? departmentId = null,
        string? mobile = "keep", string? email = "keep", string? rowVersion = null)
    {
        var s = repo.Stored(id);
        return new UpdateEmployeeRequest
        {
            EmployeeName = name ?? s.EmployeeName,
            Designation = designation ?? s.Designation,
            DepartmentId = departmentId ?? s.DepartmentId,
            Mobile = mobile == "keep" ? s.Mobile : mobile,
            Email = email == "keep" ? s.Email : email,
            RowVersion = rowVersion ?? Rv(repo, id),
        };
    }

    // ================================================================ list / get

    [Fact]
    public async Task GetAll_ReturnsAPagedResult_OrderedByName_WithDepartmentNames()
    {
        var result = await Create().Service.GetAllAsync(new EmployeeListQuery { PageNumber = 1, PageSize = 2 }, CancellationToken.None);

        Assert.Equal(3, result.TotalCount);
        Assert.Equal(new[] { "Karthik Raja", "Old Hand" }, result.Items.Select(i => i.EmployeeName));
        Assert.Equal("Old Dept", result.Items[0].DepartmentName);
        Assert.False(result.Items[0].DepartmentIsActive);
        Assert.All(result.Items, i => Assert.False(string.IsNullOrEmpty(i.RowVersion)));
    }

    [Fact]
    public async Task GetAll_FiltersByStatus_AndSearchesCodeAndName()
    {
        var s = Create();

        Assert.Equal(2, (await s.Service.GetAllAsync(new EmployeeListQuery { IsActive = true }, CancellationToken.None)).TotalCount);
        Assert.Equal("Old Hand", Assert.Single((await s.Service.GetAllAsync(new EmployeeListQuery { IsActive = false }, CancellationToken.None)).Items).EmployeeName);
        Assert.Equal("Ravi Kumar", Assert.Single((await s.Service.GetAllAsync(new EmployeeListQuery { Search = "ravi" }, CancellationToken.None)).Items).EmployeeName);
        Assert.Equal("Karthik Raja", Assert.Single((await s.Service.GetAllAsync(new EmployeeListQuery { Search = "emp-0002" }, CancellationToken.None)).Items).EmployeeName);
    }

    [Fact]
    public async Task GetById_ReturnsEveryField_IncludingDepartmentAndAuditColumns()
    {
        var dto = await Create().Service.GetByIdAsync(1, CancellationToken.None);

        Assert.Equal("EMP-0001", dto.EmployeeCode);
        Assert.Equal("Ravi Kumar", dto.EmployeeName);
        Assert.Equal("Maintenance Engineer", dto.Designation);
        Assert.Equal(1, dto.DepartmentId);
        Assert.Equal("Injection Moulding", dto.DepartmentName);
        Assert.True(dto.DepartmentIsActive);
        Assert.Equal("9840011122", dto.Mobile);
        Assert.Equal("ravi@example.com", dto.Email);
        Assert.True(dto.IsActive);
        Assert.Equal(1, dto.CreatedBy);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), dto.CreatedAt);
    }

    [Fact]
    public async Task GetById_ThrowsNotFound_ForAnUnknownEmployee()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => Create().Service.GetByIdAsync(999, CancellationToken.None));
    }

    // ================================================================ create

    [Fact]
    public async Task Create_IssuesTheCode_TrimsFields_SetsActiveAndCreatedBy_AndAudits()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(NewEmployee(name: "  Priya Dharshini ", designation: " QC Inspector ", mobile: " 9840011128 ", email: "  "), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("EMP-0004", dto.EmployeeCode);
        Assert.Equal("Priya Dharshini", dto.EmployeeName);
        Assert.Equal("QC Inspector", dto.Designation);
        Assert.Equal("9840011128", dto.Mobile);
        Assert.Null(dto.Email); // blank -> null
        Assert.Equal(2, dto.DepartmentId);
        Assert.Equal("Mixing", dto.DepartmentName);
        Assert.True(dto.IsActive);
        Assert.Equal(1, dto.CreatedBy);
        Assert.Equal(FixedClock.Now, dto.CreatedAt);
        Assert.Null(dto.UpdatedBy);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("EmployeeCreated", entry.Action);
        Assert.Equal("Employee", entry.Module);
        Assert.Equal("Employee", entry.EntityName);
        Assert.Equal(dto.EmployeeId, entry.EntityId);
        Assert.Equal("EMP-0004", entry.RecordRef);
        Assert.Equal(1, entry.UserId);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Equal("10.0.0.5", entry.IpAddress);
        Assert.Contains("Priya Dharshini", entry.Description);
        Assert.DoesNotContain("9840011128", entry.Description);
    }

    [Fact]
    public async Task Create_RejectsMissingRequiredFields_WithAllErrors_AndWritesNothing()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.CreateAsync(NewEmployee(name: " ", designation: "", departmentId: 0), 1, null, CancellationToken.None));

        Assert.Contains("EmployeeName is required.", ex.Errors);
        Assert.Contains("Designation is required.", ex.Errors);
        Assert.Contains("DepartmentId is required.", ex.Errors);
        Assert.Equal(0, s.Employees.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_EnforcesTheColumnLengths()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(
            NewEmployee(name: new string('n', 101), designation: new string('d', 101), mobile: new string('9', 16), email: new string('e', 151)),
            1, null, CancellationToken.None));

        Assert.Contains("EmployeeName must be at most 100 characters.", ex.Errors);
        Assert.Contains("Designation must be at most 100 characters.", ex.Errors);
        Assert.Contains("Mobile must be at most 15 characters.", ex.Errors);
        Assert.Contains("Email must be at most 150 characters.", ex.Errors);

        var ok = await s.Service.CreateAsync(
            NewEmployee(name: new string('n', 100), designation: new string('d', 100), mobile: new string('9', 15), email: new string('e', 150)),
            1, null, CancellationToken.None);
        Assert.Equal(100, ok.EmployeeName.Length);
    }

    [Fact]
    public async Task Create_ThrowsNotFound_ForAnUnknownDepartment_AndWritesNothing()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(NewEmployee(departmentId: 999), 1, null, CancellationToken.None));

        Assert.Equal(0, s.Employees.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_RejectsAnInactiveDepartment_WithValidation_AndWritesNothing()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(NewEmployee(departmentId: 3), 1, null, CancellationToken.None));

        Assert.Contains("The selected department is not active.", ex.Errors);
        Assert.Equal(0, s.Employees.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_AllowsTwoEmployeesWithTheSameName_NameUniquenessIsNotARule()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(NewEmployee(name: "Ravi Kumar"), 1, null, CancellationToken.None);

        Assert.NotEqual("EMP-0001", dto.EmployeeCode);
        Assert.Equal(4, s.Employees.Count);
    }

    // ================================================================ update

    [Fact]
    public async Task Update_ChangesTheEditableFields_OnlyThoseColumns_AndSetsUpdatedBy()
    {
        var s = Create();
        var before = s.Employees.Stored(1);
        var (code, active, createdBy, createdAt, oldVersion) = (before.EmployeeCode, before.IsActive, before.CreatedBy, before.CreatedAt, Rv(s.Employees, 1));

        var dto = await s.Service.UpdateAsync(1, Edit(s.Employees, 1, name: "Ravi K", designation: "Senior Engineer", departmentId: 2, mobile: "9000000000", email: null), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal("Ravi K", dto.EmployeeName);
        Assert.Equal("Senior Engineer", dto.Designation);
        Assert.Equal(2, dto.DepartmentId);
        Assert.Equal("Mixing", dto.DepartmentName);
        Assert.Equal("9000000000", dto.Mobile);
        Assert.Null(dto.Email);
        var after = s.Employees.Stored(1);
        Assert.Equal((code, active, createdBy, createdAt), (after.EmployeeCode, after.IsActive, after.CreatedBy, after.CreatedAt));
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(FixedClock.Now, after.UpdatedAt);
        Assert.NotEqual(oldVersion, dto.RowVersion);
    }

    [Fact]
    public async Task Update_AuditsChangedFieldNames_WithValuesOnlyForNonContactFields()
    {
        var s = Create();

        await s.Service.UpdateAsync(1, Edit(s.Employees, 1, designation: "Senior Engineer", departmentId: 2, mobile: "9000000000", email: "new@example.com"), 1, null, CancellationToken.None);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("EmployeeUpdated", entry.Action);
        Assert.Equal("EMP-0001", entry.RecordRef);
        Assert.Contains("Changed: designation, department, mobile, email.", entry.Description);
        Assert.Contains(entry.Details, d => d is { FieldName: "designation", OldValue: "Maintenance Engineer", NewValue: "Senior Engineer" });
        Assert.Contains(entry.Details, d => d is { FieldName: "department_code", OldValue: "DEP-0001", NewValue: "DEP-0002" });
        // Contact data never lands in the audit trail as values.
        var everything = System.Text.Json.JsonSerializer.Serialize(entry);
        Assert.DoesNotContain("9840011122", everything);
        Assert.DoesNotContain("9000000000", everything);
        Assert.DoesNotContain("new@example.com", everything);
        Assert.DoesNotContain("ravi@example.com", everything);
    }

    [Fact]
    public async Task Update_KeepingAnInactiveDepartment_IsAllowed_ButChoosingOneIsNot()
    {
        var s = Create();

        // Employee 2 is in department 3, which has since been deactivated: an unrelated edit still saves.
        var kept = await s.Service.UpdateAsync(2, Edit(s.Employees, 2, designation: "Senior Operator"), 1, null, CancellationToken.None);
        Assert.Equal(3, kept.DepartmentId);
        Assert.False(kept.DepartmentIsActive);

        // Employee 1 moving INTO the inactive department is refused.
        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.UpdateAsync(1, Edit(s.Employees, 1, departmentId: 3), 1, null, CancellationToken.None));
        Assert.Contains("The selected department is not active.", ex.Errors);
        Assert.Equal(1, s.Employees.Stored(1).DepartmentId);
    }

    [Fact]
    public async Task Update_ThrowsNotFound_ForAnUnknownEmployee_OrAnUnknownNewDepartment()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            s.Service.UpdateAsync(999, new UpdateEmployeeRequest { EmployeeName = "X", Designation = "Y", DepartmentId = 1, RowVersion = "AQ==" }, 1, null, CancellationToken.None));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            s.Service.UpdateAsync(1, Edit(s.Employees, 1, departmentId: 999), 1, null, CancellationToken.None));

        Assert.Equal(0, s.Employees.UpdateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_ValidatesFields_AndTheRowVersion()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.UpdateAsync(1, new UpdateEmployeeRequest { EmployeeName = "", Designation = " ", DepartmentId = 1, RowVersion = "" }, 1, null, CancellationToken.None));
        Assert.Contains("EmployeeName is required.", ex.Errors);
        Assert.Contains("Designation is required.", ex.Errors);
        Assert.Contains("RowVersion is required.", ex.Errors);

        var bad = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.UpdateAsync(1, Edit(s.Employees, 1, rowVersion: "not base64 !!"), 1, null, CancellationToken.None));
        Assert.Contains("RowVersion is not valid.", bad.Errors);

        Assert.Equal(0, s.Employees.UpdateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_WithAStaleRowVersion_ThrowsConflict_AndDoesNotOverwriteOrAudit()
    {
        var s = Create();
        var stale = Edit(s.Employees, 1, name: "Stale edit");
        s.Employees.SimulateConcurrentModification(1);

        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(1, stale, 1, null, CancellationToken.None));

        Assert.Contains("modified by another user", ex.Message);
        Assert.Equal("Ravi Kumar", s.Employees.Stored(1).EmployeeName);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ deactivate

    [Fact]
    public async Task Deactivate_SetsInactive_KeepsTheRow_ChangesNothingElse_AndAudits()
    {
        var s = Create();
        var before = s.Employees.Stored(1);
        var snapshot = (before.EmployeeCode, before.EmployeeName, before.Designation, before.DepartmentId, before.Mobile, before.Email, before.CreatedBy, before.CreatedAt);

        var dto = await s.Service.DeactivateAsync(1, 1, "10.0.0.5", CancellationToken.None);

        Assert.False(dto.IsActive);
        var after = s.Employees.Stored(1);
        Assert.False(after.IsActive);
        Assert.Equal(snapshot, (after.EmployeeCode, after.EmployeeName, after.Designation, after.DepartmentId, after.Mobile, after.Email, after.CreatedBy, after.CreatedAt));
        Assert.Equal(1, after.UpdatedBy);
        Assert.Equal(FixedClock.Now, after.UpdatedAt);
        Assert.Equal(3, s.Employees.Count);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("EmployeeDeactivated", entry.Action);
        Assert.Equal("EMP-0001", entry.RecordRef);
        Assert.Equal(1, entry.EntityId);
        Assert.Equal("10.0.0.5", entry.IpAddress);
    }

    [Fact]
    public async Task Deactivate_ThrowsNotFound_ForUnknown_AndConflict_ForAlreadyInactive_WritingNothing()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.DeactivateAsync(999, 1, null, CancellationToken.None));
        var ex = await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(3, 1, null, CancellationToken.None));

        Assert.Contains("already inactive", ex.Message);
        Assert.Equal(0, s.Employees.DeactivateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Deactivate_WhenTheRowChangesBetweenTheReadAndTheSave_ThrowsConflict_AndStaysActive()
    {
        var s = Create();
        s.Employees.BeforeWrite = s.Employees.SimulateConcurrentModification;

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.DeactivateAsync(1, 1, null, CancellationToken.None));

        Assert.True(s.Employees.Stored(1).IsActive);
        Assert.Empty(s.Audit.Entries);
    }

    // ================================================================ audit failure behaviour

    [Fact]
    public async Task WhenTheActingUsersNameCannotBeResolved_TheOperationStillSucceeds_AuditedAsUnknown()
    {
        var s = Create();
        s.Users.ThrowOnGetByIdCall = 1;

        var dto = await s.Service.CreateAsync(NewEmployee(), 1, null, CancellationToken.None);

        Assert.Equal("EMP-0004", dto.EmployeeCode);
        Assert.Equal("Unknown", Assert.Single(s.Audit.Entries).UserName);
    }

    [Fact]
    public async Task WhenWritingTheAuditRowFails_EveryOperationStillSucceeds()
    {
        var audit = new AuditLogService(new ThrowingAuditRepository(), new FixedClock(), NullLogger<AuditLogService>.Instance);
        var s = Create(auditOverride: audit);

        var created = await s.Service.CreateAsync(NewEmployee(), 1, null, CancellationToken.None);
        var updated = await s.Service.UpdateAsync(created.EmployeeId, Edit(s.Employees, created.EmployeeId, name: "Priya D"), 1, null, CancellationToken.None);
        var deactivated = await s.Service.DeactivateAsync(created.EmployeeId, 1, null, CancellationToken.None);

        Assert.Equal("Priya D", updated.EmployeeName);
        Assert.False(deactivated.IsActive);
        Assert.False(s.Employees.Stored(created.EmployeeId).IsActive);
    }

    private sealed class ThrowingAuditRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit persistence failure.");
    }
}
