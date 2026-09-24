using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Create/update/list behavior of the REAL UserService against in-memory fakes (the real repository needs a
/// live SQL Server). Seed data: users 1 (Admin, acting user) and 2 (ravi); roles 1 ADMIN, 2 MAINT_MANAGER,
/// 3 TEST (dynamically created), 4 OLD_ROLE (inactive). See UserTestFakes.cs.
/// </summary>
public class UserServiceTests
{
    private sealed record Sut(UserService Service, InMemoryUserRepository Users, RecordingAuditLog Audit, Sha256Hasher Hasher);

    private static Sut Create(IAuditLogService? auditOverride = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var audit = new RecordingAuditLog();
        var hasher = new Sha256Hasher();
        var service = new UserService(users, roles, hasher, new FixedClock(), auditOverride ?? audit, NullLogger<UserService>.Instance);
        return new Sut(service, users, audit, hasher);
    }

    private static CreateUserRequest NewUser(
        string loginId = "tester2", string userName = "Tester Two", int roleId = 3, string password = "S3cret pass!", bool isActive = true,
        string? email = null, string? mobile = null) =>
        new() { LoginId = loginId, UserName = userName, RoleId = roleId, Password = password, IsActive = isActive, Email = email, Mobile = mobile };

    private static string Rv(InMemoryUserRepository users, int userId) => Convert.ToBase64String(users.Stored(userId).RowVersion);

    // Update request defaulting to "no change" for user 2, so each test states only what it changes.
    private static UpdateUserRequest Edit(
        Sut s, int userId = 2, string? loginId = null, string? userName = null, int? roleId = null, bool? isActive = null,
        string? email = "keep", string? mobile = "keep", string? newPassword = null, string? rowVersion = null)
    {
        var u = s.Users.Stored(userId);
        return new UpdateUserRequest
        {
            LoginId = loginId ?? u.LoginId,
            UserName = userName ?? u.UserName,
            Email = email == "keep" ? u.Email : email,
            Mobile = mobile == "keep" ? u.Mobile : mobile,
            RoleId = roleId ?? u.RoleId,
            IsActive = isActive ?? u.IsActive,
            NewPassword = newPassword,
            RowVersion = rowVersion ?? Rv(s.Users, userId),
        };
    }

    // ================================================================ CREATE

    [Fact]
    public async Task Create_CreatesTheUser_WithRole_Status_AuditColumns_AndAnIssuedUserCode()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(NewUser(email: "t2@example.com", mobile: "9000000000"), actingUserId: 1, "10.0.0.5", CancellationToken.None);

        Assert.True(dto.UserId > 0);
        Assert.Equal("USR-0003", dto.UserCode); // issued by the repository (the document sequence), never by the client
        Assert.Equal("tester2", dto.LoginId);
        Assert.Equal("Tester Two", dto.UserName);
        Assert.Equal(3, dto.RoleId);
        Assert.Equal("test", dto.RoleName);
        Assert.Equal("t2@example.com", dto.Email);
        Assert.True(dto.IsActive);
        Assert.True(dto.MustChangePassword); // admin-set password is temporary
        Assert.NotEmpty(dto.RowVersion);

        var stored = s.Users.Stored(dto.UserId);
        Assert.Equal(3, stored.RoleId);
        Assert.Equal(1, stored.CreatedBy);
        Assert.Equal(FixedClock.Now, stored.CreatedAt);
        Assert.Null(stored.UpdatedBy);
    }

    [Fact]
    public async Task Create_HashesThePassword_AndNeverPersistsOrReturnsThePlaintext()
    {
        var s = Create();
        const string password = "S3cret pass!";

        var dto = await s.Service.CreateAsync(NewUser(password: password), 1, null, CancellationToken.None);

        var stored = s.Users.Stored(dto.UserId);
        Assert.Equal(Sha256Hasher.Hash(password), stored.PasswordHash);
        Assert.NotEqual(password, stored.PasswordHash);
        Assert.DoesNotContain(password, JsonSerializer.Serialize(stored)); // nowhere in the persisted user
        Assert.DoesNotContain(password, JsonSerializer.Serialize(dto));    // nor in the response
        Assert.Equal(new[] { password }, s.Hasher.Hashed);                 // hashed exactly once, verbatim (not trimmed)
        Assert.True(s.Hasher.VerifyPassword(stored.PasswordHash, password));
    }

    [Fact]
    public async Task Create_TrimsAndNormalizesFields_AndBlankOptionalsBecomeNull()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(
            new CreateUserRequest { LoginId = "  tester2  ", UserName = "  Tester Two ", RoleId = 2, Password = "x", Email = "   ", Mobile = "" },
            1, null, CancellationToken.None);

        Assert.Equal("tester2", dto.LoginId);
        Assert.Equal("Tester Two", dto.UserName);
        Assert.Null(dto.Email);
        Assert.Null(dto.Mobile);
    }

    [Fact]
    public async Task Create_AppliesNoPasswordPolicyBeyondNotEmpty_BecausePolicyQ37IsUnresolved()
    {
        var s = Create();

        // Short, no complexity, whitespace included: nothing is invented - only "not empty" applies (AuthService's own rule).
        var dto = await s.Service.CreateAsync(NewUser(password: " a "), 1, null, CancellationToken.None);

        Assert.True(s.Hasher.VerifyPassword(s.Users.Stored(dto.UserId).PasswordHash, " a "));
    }

    [Fact]
    public async Task Create_HonoursIsActiveFalse()
    {
        var s = Create();

        var dto = await s.Service.CreateAsync(NewUser(isActive: false), 1, null, CancellationToken.None);

        Assert.False(dto.IsActive);
    }

    [Fact]
    public async Task Create_RejectsAnUnknownRole_With404()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.CreateAsync(NewUser(roleId: 999), 1, null, CancellationToken.None));

        Assert.Equal(0, s.Users.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_RejectsAnInactiveRole_With400()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(NewUser(roleId: 4), 1, null, CancellationToken.None));

        Assert.Contains("not active", ex.Errors[0]);
        Assert.Equal(0, s.Users.AddCalls);
    }

    [Theory]
    [InlineData("tester2")]
    [InlineData("ADMIN")]      // case-insensitive, like the login_id collation
    [InlineData("  Ravi  ")]
    public async Task Create_RejectsADuplicateLoginId_With409(string loginId)
    {
        var s = Create();
        var before = s.Users.Count;
        await s.Service.CreateAsync(NewUser(loginId: "tester2"), 1, null, CancellationToken.None);
        s.Audit.Entries.Clear();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            s.Service.CreateAsync(NewUser(loginId: loginId, userName: "Someone Else"), 1, null, CancellationToken.None));

        Assert.Contains("already exists", ex.Message);
        Assert.Equal(before + 1, s.Users.Count);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_ReportsAllValidationErrorsTogether_AndWritesNothing()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.CreateAsync(
            new CreateUserRequest { LoginId = " ", UserName = new string('n', 101), Email = new string('e', 151), Mobile = new string('9', 16), RoleId = 2, Password = "" },
            1, null, CancellationToken.None));

        Assert.Contains("LoginId is required.", ex.Errors);
        Assert.Contains(ex.Errors, e => e.StartsWith("UserName must be at most"));
        Assert.Contains(ex.Errors, e => e.StartsWith("Email must be at most"));
        Assert.Contains(ex.Errors, e => e.StartsWith("Mobile must be at most"));
        Assert.Contains("Password is required.", ex.Errors);
        Assert.Equal(0, s.Users.AddCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Create_WritesExactlyOneUserCreatedAuditEntry_WithoutAnySecret()
    {
        var s = Create();
        const string password = "S3cret pass!";

        var dto = await s.Service.CreateAsync(NewUser(password: password), actingUserId: 1, ipAddress: "10.0.0.5", CancellationToken.None);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("UserCreated", entry.Action);
        Assert.Equal("Security", entry.Module);
        Assert.Equal("User", entry.EntityName);
        Assert.Equal(dto.UserId, entry.EntityId);
        Assert.Equal("USR-0003", entry.RecordRef);
        Assert.Equal(1, entry.UserId);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Equal("10.0.0.5", entry.IpAddress);
        Assert.Contains("TEST", entry.Description); // the assigned role's code
        var everything = JsonSerializer.Serialize(entry);
        Assert.DoesNotContain(password, everything);
        Assert.DoesNotContain(Sha256Hasher.Hash(password), everything);
        Assert.DoesNotContain("sha256", everything, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Create_Succeeds_EvenIfTheAuditDatabaseWriteFails()
    {
        var failingAudit = new AuditLogService(new ThrowingAuditRepository(), new FixedClock(), NullLogger<AuditLogService>.Instance);
        var s = Create(failingAudit);

        var dto = await s.Service.CreateAsync(NewUser(), 1, null, CancellationToken.None);

        Assert.NotNull(s.Users.Stored(dto.UserId));
    }

    [Fact]
    public async Task Create_Succeeds_EvenIfTheAuditUserNameLookupFails()
    {
        var s = Create();
        s.Users.ThrowOnGetByIdCall = 1; // create's only GetById call is the audit user-name lookup

        var dto = await s.Service.CreateAsync(NewUser(), 1, null, CancellationToken.None);

        Assert.True(dto.UserId > 0);
        Assert.Equal("Unknown", Assert.Single(s.Audit.Entries).UserName);
    }

    [Fact]
    public void RequestsAndResponses_CannotCarryClientControlledOrSecretFields()
    {
        var forbiddenOnCreate = new[]
        {
            "UserId", "UserCode", "PasswordHash", "CreatedAt", "CreatedBy", "UpdatedAt", "UpdatedBy", "RowVersion",
            "MustChangePassword", "FailedLoginCount", "LockoutEndAt", "LastLoginAt", "IsSystemRole", "EmployeeId", "DepartmentId",
        };
        var create = typeof(CreateUserRequest).GetProperties().Select(p => p.Name).ToList();
        Assert.Empty(create.Intersect(forbiddenOnCreate));

        var update = typeof(UpdateUserRequest).GetProperties().Select(p => p.Name).ToList();
        Assert.Empty(update.Intersect(forbiddenOnCreate.Where(n => n != "RowVersion"))); // RowVersion is the concurrency token the caller must echo

        var response = typeof(UserDto).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("PasswordHash", response);
        Assert.DoesNotContain("Password", response);
        Assert.DoesNotContain("NewPassword", response);
        Assert.DoesNotContain("FailedLoginCount", response);
        Assert.DoesNotContain("LockoutEndAt", response);
    }

    // ================================================================ UPDATE

    [Fact]
    public async Task Update_UpdatesTheEditableFields_AndSetsUpdatedByAndUpdatedAt()
    {
        var s = Create();

        var dto = await s.Service.UpdateAsync(
            2, Edit(s, userName: "Ravi K", email: "new@example.com", mobile: "9111111111", isActive: false), 1, null, CancellationToken.None);

        Assert.Equal("Ravi K", dto.UserName);
        Assert.False(dto.IsActive);
        var stored = s.Users.Stored(2);
        Assert.Equal("new@example.com", stored.Email);
        Assert.Equal("9111111111", stored.Mobile);
        Assert.False(stored.IsActive);
        Assert.Equal(1, stored.UpdatedBy);
        Assert.Equal(FixedClock.Now, stored.UpdatedAt);
    }

    [Fact]
    public async Task Update_NeverTouchesUserCode_Lockout_LastLogin_OrCreationColumns()
    {
        var s = Create();

        await s.Service.UpdateAsync(2, Edit(s, userName: "Ravi K"), 1, null, CancellationToken.None);

        var stored = s.Users.Stored(2);
        Assert.Equal("USR-0002", stored.UserCode);
        Assert.Equal(2, stored.FailedLoginCount);
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), stored.LastLoginAt);
        Assert.Equal(1, stored.CreatedBy);
        Assert.Equal(new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), stored.CreatedAt);
    }

    [Fact]
    public async Task Update_ChangesTheRole_ToAnyActiveRole_IncludingADynamicallyCreatedOne()
    {
        var s = Create();

        var dto = await s.Service.UpdateAsync(2, Edit(s, roleId: 3), 1, null, CancellationToken.None);

        Assert.Equal(3, dto.RoleId);
        Assert.Equal("test", dto.RoleName);
        Assert.Equal(3, s.Users.Stored(2).RoleId);
    }

    [Fact]
    public async Task Update_RejectsAnUnknownRole_With404_AndAnInactiveRole_With400()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.UpdateAsync(2, Edit(s, roleId: 999), 1, null, CancellationToken.None));
        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(2, Edit(s, roleId: 4), 1, null, CancellationToken.None));

        Assert.Contains("not active", ex.Errors[0]);
        Assert.Equal(2, s.Users.Stored(2).RoleId);
        Assert.Equal(0, s.Users.UpdateCalls);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_KeepingTheCurrentRole_NeedsNoRoleCheck_EvenIfThatRoleIsNowInactive()
    {
        var s = Create();
        s.Users.Stored(2).RoleId = 4; // the user sits in a role that has since been deactivated

        var dto = await s.Service.UpdateAsync(2, Edit(s, userName: "Ravi K"), 1, null, CancellationToken.None);

        Assert.Equal("Ravi K", dto.UserName);
        Assert.Equal(4, dto.RoleId);
    }

    [Fact]
    public async Task Update_ChangesThePassword_OnlyWhenANewOneIsSupplied_AndMarksItTemporary()
    {
        var s = Create();
        var oldHash = s.Users.Stored(2).PasswordHash;

        await s.Service.UpdateAsync(2, Edit(s, newPassword: "Brand-new pw"), 1, null, CancellationToken.None);

        var stored = s.Users.Stored(2);
        Assert.NotEqual(oldHash, stored.PasswordHash);
        Assert.True(s.Hasher.VerifyPassword(stored.PasswordHash, "Brand-new pw"));
        Assert.True(stored.MustChangePassword);
        Assert.DoesNotContain("Brand-new pw", JsonSerializer.Serialize(stored));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Update_LeavesThePasswordUnchanged_WhenNoNewPasswordIsSupplied(string? newPassword)
    {
        var s = Create();
        var oldHash = s.Users.Stored(2).PasswordHash;

        await s.Service.UpdateAsync(2, Edit(s, userName: "Ravi K", newPassword: newPassword), 1, null, CancellationToken.None);

        var stored = s.Users.Stored(2);
        Assert.Equal(oldHash, stored.PasswordHash);
        Assert.False(stored.MustChangePassword);
        Assert.Empty(s.Hasher.Hashed); // the hasher was not even invoked
    }

    [Fact]
    public async Task Update_RejectsALoginIdBelongingToAnotherUser_With409_ButAllowsKeepingOrRecasingItsOwn()
    {
        var s = Create();

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(2, Edit(s, loginId: "admin"), 1, null, CancellationToken.None));

        var dto = await s.Service.UpdateAsync(2, Edit(s, loginId: "RAVI"), 1, null, CancellationToken.None);
        Assert.Equal("RAVI", dto.LoginId);
    }

    [Fact]
    public async Task Update_ThrowsNotFound_ForAnUnknownUser()
    {
        var s = Create();

        await Assert.ThrowsAsync<NotFoundException>(() => s.Service.UpdateAsync(999, Edit(s), 1, null, CancellationToken.None));

        Assert.Empty(s.Audit.Entries);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-base64!!")]
    public async Task Update_RequiresAValidRowVersion_With400(string rowVersion)
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() => s.Service.UpdateAsync(2, Edit(s, rowVersion: rowVersion), 1, null, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.StartsWith("RowVersion"));
        Assert.Equal(0, s.Users.UpdateCalls);
    }

    [Fact]
    public async Task Update_ValidatesFields_LikeCreate()
    {
        var s = Create();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            s.Service.UpdateAsync(2, Edit(s, loginId: " ", userName: " "), 1, null, CancellationToken.None));

        Assert.Contains("LoginId is required.", ex.Errors);
        Assert.Contains("UserName is required.", ex.Errors);
    }

    // ---- concurrency ----

    [Fact]
    public async Task Update_WithAStaleRowVersion_Returns409_AndChangesNothing()
    {
        var s = Create();
        var staleVersion = Rv(s.Users, 2);
        s.Users.SimulateConcurrentModification(2); // someone else saved after the form was loaded

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            s.Service.UpdateAsync(2, Edit(s, userName: "Late Edit", rowVersion: staleVersion), 1, null, CancellationToken.None));

        Assert.Equal("The user was modified by another user. Refresh the user and try again.", ex.Message);
        Assert.Equal("Ravi Kumar", s.Users.Stored(2).UserName);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_ThatLosesARaceDuringTheSave_Returns409_AndDoesNotAudit()
    {
        var s = Create();
        s.Users.BeforeUpdate = id => s.Users.SimulateConcurrentModification(id);

        await Assert.ThrowsAsync<ConflictException>(() => s.Service.UpdateAsync(2, Edit(s, userName: "Late Edit"), 1, null, CancellationToken.None));

        Assert.Equal("Ravi Kumar", s.Users.Stored(2).UserName);
        Assert.Empty(s.Audit.Entries);
    }

    [Fact]
    public async Task Update_ReturnsTheNewRowVersion_SoTheNextEditCanSucceed_AndTheOldOneIsThenStale()
    {
        var s = Create();
        var first = await s.Service.UpdateAsync(2, Edit(s, userName: "One"), 1, null, CancellationToken.None);

        // Editing again with the version from the response works...
        var second = await s.Service.UpdateAsync(2, Edit(s, userName: "Two", rowVersion: first.RowVersion), 1, null, CancellationToken.None);
        Assert.NotEqual(first.RowVersion, second.RowVersion);

        // ...but reusing the first version now is stale.
        await Assert.ThrowsAsync<ConflictException>(() =>
            s.Service.UpdateAsync(2, Edit(s, userName: "Three", rowVersion: first.RowVersion), 1, null, CancellationToken.None));
    }

    // ---- audit ----

    [Fact]
    public async Task Update_WritesExactlyOneUserUpdatedEntry_ListingOnlyFieldNames()
    {
        var s = Create();

        await s.Service.UpdateAsync(2, Edit(s, userName: "Ravi K", email: "secret-new@example.com", newPassword: "Brand-new pw"), 1, "10.0.0.5", CancellationToken.None);

        var entry = Assert.Single(s.Audit.Entries);
        Assert.Equal("UserUpdated", entry.Action);
        Assert.Equal("Security", entry.Module);
        Assert.Equal("User", entry.EntityName);
        Assert.Equal(2, entry.EntityId);
        Assert.Equal("USR-0002", entry.RecordRef);
        Assert.Equal(1, entry.UserId);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Equal("10.0.0.5", entry.IpAddress);
        Assert.Contains("Changed: UserName, Email, Password.", entry.Description);
        var everything = JsonSerializer.Serialize(entry);
        Assert.DoesNotContain("Brand-new pw", everything);
        Assert.DoesNotContain("secret-new@example.com", everything); // field names only, never values
        Assert.DoesNotContain("sha256", everything, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Update_ThatChangesTheRole_AlsoWritesUserRoleChanged_WithOldToNewRoleCodes()
    {
        var s = Create();

        await s.Service.UpdateAsync(2, Edit(s, roleId: 3), 1, "10.0.0.5", CancellationToken.None);

        Assert.Equal(new[] { "UserUpdated", "UserRoleChanged" }, s.Audit.Entries.Select(e => e.Action).ToArray());
        var change = s.Audit.Entries[1];
        Assert.Equal("Security", change.Module);
        Assert.Equal("User", change.EntityName);
        Assert.Equal(2, change.EntityId);
        Assert.Equal("USR-0002", change.RecordRef);
        Assert.Equal("Sakthi", change.UserName);
        Assert.Contains("Role: MAINT_MANAGER -> TEST", change.Description);
        Assert.Equal(new AuditLogDetailEntry("role_code", "MAINT_MANAGER", "TEST"), Assert.Single(change.Details));
    }

    [Fact]
    public async Task Update_WithoutARoleChange_WritesNoUserRoleChangedEntry()
    {
        var s = Create();

        await s.Service.UpdateAsync(2, Edit(s, userName: "Ravi K"), 1, null, CancellationToken.None);

        Assert.DoesNotContain(s.Audit.Entries, e => e.Action == "UserRoleChanged");
    }

    [Fact]
    public async Task Update_Succeeds_EvenIfTheAuditDatabaseWriteFails()
    {
        var failingAudit = new AuditLogService(new ThrowingAuditRepository(), new FixedClock(), NullLogger<AuditLogService>.Instance);
        var s = Create(failingAudit);

        var dto = await s.Service.UpdateAsync(2, Edit(s, userName: "Ravi K", roleId: 3), 1, null, CancellationToken.None);

        Assert.Equal("Ravi K", dto.UserName);
        Assert.Equal(3, s.Users.Stored(2).RoleId);
    }

    // ================================================================ LIST / GET

    [Fact]
    public async Task GetAll_ReturnsTheStoredUsers_WithTheirRealRoleNames_AndNoSecrets()
    {
        var s = Create();
        s.Users.Stored(2).RoleId = 3; // a dynamically created role

        var page = await s.Service.GetAllAsync(new PaginationRequest { PageNumber = 1, PageSize = 10 }, CancellationToken.None);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("test", page.Items.Single(u => u.LoginId == "ravi").RoleName);
        Assert.Equal("Administrator", page.Items.Single(u => u.LoginId == "Admin").RoleName);
        Assert.All(page.Items, u => Assert.NotEmpty(u.RowVersion));
        var json = JsonSerializer.Serialize(page);
        Assert.DoesNotContain("sha256", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetById_ReturnsTheRowVersion_ThatUpdateThenRequires()
    {
        var s = Create();

        var dto = await s.Service.GetByIdAsync(2, CancellationToken.None);
        var updated = await s.Service.UpdateAsync(2, Edit(s, userName: "Ravi K", rowVersion: dto.RowVersion), 1, null, CancellationToken.None);

        Assert.Equal("Ravi K", updated.UserName);
    }

    private sealed class ThrowingAuditRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit DB failure.");
    }
}
