using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// DeactivateAsync tests for the real RoleService. Partial-class extension of
/// <see cref="RoleServiceTests"/> so it shares that file's fakes. Roles in <c>SampleRoles()</c>:
/// id 1 ADMIN (system role), id 2 MAINT_MANAGER (ordinary role, active).
/// </summary>
public partial class RoleServiceTests
{
    private static FakeRoleRepository RepoWithOrdinaryRoleDetails()
    {
        var roles = SampleRoles();
        roles[1].Description = "Maintenance, reports and dashboard";
        roles[1].CreatedBy = 3;
        roles[1].CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        return new FakeRoleRepository(roles);
    }

    [Fact]
    public async Task DeactivateAsync_SetsIsActiveFalse_AndReturnsTheDeactivatedRole()
    {
        var repo = RepoWithOrdinaryRoleDetails();

        var dto = await CreateService(repo).DeactivateAsync(2, actingUserId: 7, ipAddress: null, CancellationToken.None);

        Assert.Equal(2, dto.RoleId);
        Assert.False(dto.IsActive);
        Assert.False(repo.Stored(2).IsActive);
    }

    [Fact]
    public async Task DeactivateAsync_KeepsTheRow_AndChangesNoOtherRoleField()
    {
        var repo = RepoWithOrdinaryRoleDetails();
        var before = repo.Stored(2);
        var (id, code, name, description, isSystem, createdBy, createdAt) =
            (before.RoleId, before.RoleCode, before.RoleName, before.Description, before.IsSystemRole, before.CreatedBy, before.CreatedAt);

        await CreateService(repo).DeactivateAsync(2, 7, null, CancellationToken.None);

        var after = repo.Stored(2); // still there - Stored() throws if the row had been removed
        Assert.Equal(id, after.RoleId);
        Assert.Equal(code, after.RoleCode);
        Assert.Equal(name, after.RoleName);
        Assert.Equal(description, after.Description);
        Assert.Equal(isSystem, after.IsSystemRole);
        Assert.Equal(createdBy, after.CreatedBy);
        Assert.Equal(createdAt, after.CreatedAt);
        var (_, totalAfter) = await repo.GetAllAsync(new PaginationRequest { PageNumber = 1, PageSize = 100 }, CancellationToken.None);
        Assert.Equal(2, totalAfter); // no row was deleted
    }

    [Fact]
    public async Task DeactivateAsync_SetsUpdatedByAndUpdatedAt()
    {
        var repo = RepoWithOrdinaryRoleDetails();

        await CreateService(repo).DeactivateAsync(2, actingUserId: 7, null, CancellationToken.None);

        Assert.Equal(7, repo.Stored(2).UpdatedBy);
        Assert.Equal(FixedNow, repo.Stored(2).UpdatedAt);
    }

    [Fact]
    public async Task DeactivateAsync_ThrowsNotFound_WhenRoleDoesNotExist()
    {
        var repo = RepoWithOrdinaryRoleDetails();
        var audit = new FakeAuditLogService();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateService(repo, audit).DeactivateAsync(999, 7, null, CancellationToken.None));

        Assert.Equal(0, repo.DeactivateCallCount);
        Assert.Empty(audit.LoggedEntries);
    }

    // ---- system roles ----

    [Fact]
    public async Task DeactivateAsync_RefusesASystemRole_WithConflict_WritingAndAuditingNothing()
    {
        var repo = RepoWithOrdinaryRoleDetails();
        var audit = new FakeAuditLogService();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService(repo, audit).DeactivateAsync(1, 7, null, CancellationToken.None));

        Assert.Contains("system role", ex.Message);
        Assert.True(repo.Stored(1).IsActive);
        Assert.Equal(0, repo.DeactivateCallCount);
        Assert.Empty(audit.LoggedEntries);
    }

    [Fact]
    public async Task DeactivateAsync_DecidesFromIsSystemRole_NotFromTheRoleName()
    {
        // A system role that is NOT called ADMIN is refused; an ordinary role that IS called ADMIN is not.
        var roles = new List<Role>
        {
            new() { RoleId = 10, RoleCode = "BUILT_IN", RoleName = "Built In", IsSystemRole = true, IsActive = true },
            new() { RoleId = 11, RoleCode = "ADMIN", RoleName = "Administrator", IsSystemRole = false, IsActive = true },
        };
        var service = CreateService(roles);

        await Assert.ThrowsAsync<ConflictException>(() => service.DeactivateAsync(10, 7, null, CancellationToken.None));

        var dto = await service.DeactivateAsync(11, 7, null, CancellationToken.None);
        Assert.False(dto.IsActive);
    }

    // ---- active users ----

    [Fact]
    public async Task DeactivateAsync_RefusesWhileActiveUsersAreAssigned_WithAGenericConflict()
    {
        var repo = RepoWithOrdinaryRoleDetails();
        var audit = new FakeAuditLogService();
        var users = new FakeUserRepository(roleIdsWithActiveUsers: new[] { 2 });

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService(repo, audit, users).DeactivateAsync(2, 7, null, CancellationToken.None));

        Assert.Equal("The role cannot be deactivated while active users are assigned to it.", ex.Message);
        Assert.DoesNotContain("Francis", ex.Message); // no user details
        Assert.True(repo.Stored(2).IsActive);
        Assert.Equal(0, repo.DeactivateCallCount);
        Assert.Empty(audit.LoggedEntries);
    }

    [Fact]
    public async Task DeactivateAsync_IsNotBlockedByActiveUsersOfAnotherRole()
    {
        var repo = RepoWithOrdinaryRoleDetails();
        var users = new FakeUserRepository(roleIdsWithActiveUsers: new[] { 1 }); // only the ADMIN role has users

        var dto = await CreateService(repo, userRepository: users).DeactivateAsync(2, 7, null, CancellationToken.None);

        Assert.False(dto.IsActive);
    }

    // ---- already inactive ----

    [Fact]
    public async Task DeactivateAsync_OnAnAlreadyInactiveRole_IsAnExplicitConflict_NotASilentNoOp()
    {
        var roles = SampleRoles();
        roles[1].IsActive = false;
        var repo = new FakeRoleRepository(roles);
        var audit = new FakeAuditLogService();

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService(repo, audit).DeactivateAsync(2, 7, null, CancellationToken.None));

        Assert.Equal("The role is already inactive.", ex.Message);
        Assert.Equal(0, repo.DeactivateCallCount);
        Assert.Null(repo.Stored(2).UpdatedBy); // untouched
        Assert.Empty(audit.LoggedEntries);
    }

    // ---- concurrency ----

    [Fact]
    public async Task DeactivateAsync_ThrowsConflict_WhenTheRoleWasModifiedAfterItWasLoaded()
    {
        var repo = RepoWithOrdinaryRoleDetails();
        var audit = new FakeAuditLogService();
        repo.BeforeUpdate = roleId => repo.SimulateConcurrentModification(roleId);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService(repo, audit).DeactivateAsync(2, 7, null, CancellationToken.None));

        Assert.Contains("modified by another user", ex.Message);
        Assert.True(repo.Stored(2).IsActive); // the refused save changed nothing
        Assert.Empty(audit.LoggedEntries);
    }

    // ---- audit ----

    [Fact]
    public async Task DeactivateAsync_WritesExactlyOneRoleDeactivatedAuditEntry()
    {
        var audit = new FakeAuditLogService();

        await CreateService(RepoWithOrdinaryRoleDetails(), audit)
            .DeactivateAsync(2, actingUserId: 7, ipAddress: "10.0.0.5", CancellationToken.None);

        var entry = Assert.Single(audit.LoggedEntries);
        Assert.Equal(7, entry.UserId);
        Assert.Equal("Francis Xavier", entry.UserName);
        Assert.Equal("Security", entry.Module);
        Assert.Equal("RoleDeactivated", entry.Action);
        Assert.Equal("Role", entry.EntityName);
        Assert.Equal(2, entry.EntityId);
        Assert.Equal("MAINT_MANAGER", entry.RecordRef);
        Assert.Equal("10.0.0.5", entry.IpAddress);
        Assert.Contains("Francis Xavier", entry.Description);
        Assert.Contains("Maintenance Manager", entry.Description);
    }

    [Fact]
    public async Task DeactivateAsync_StillDeactivatesAndAudits_WhenActingUserCannotBeResolved()
    {
        var repo = RepoWithOrdinaryRoleDetails();
        var audit = new FakeAuditLogService();

        await CreateService(repo, audit).DeactivateAsync(2, actingUserId: null, null, CancellationToken.None);

        Assert.Null(repo.Stored(2).UpdatedBy);
        var entry = Assert.Single(audit.LoggedEntries);
        Assert.Null(entry.UserId);
        Assert.Equal("Unknown", entry.UserName);
    }

    [Fact]
    public async Task DeactivateAsync_Succeeds_EvenIfTheAuditDatabaseWriteFails()
    {
        var repo = RepoWithOrdinaryRoleDetails();

        var dto = await CreateService(repo, AuditServiceWhoseDatabaseFails()).DeactivateAsync(2, 7, null, CancellationToken.None);

        Assert.False(dto.IsActive);
        Assert.False(repo.Stored(2).IsActive);
    }

    [Fact]
    public async Task DeactivateAsync_Succeeds_EvenIfTheAuditUserNameLookupFails()
    {
        var repo = RepoWithOrdinaryRoleDetails();
        var audit = new FakeAuditLogService();

        var dto = await CreateService(repo, audit, new FakeUserRepository(lookupFails: true))
            .DeactivateAsync(2, 7, null, CancellationToken.None);

        Assert.False(dto.IsActive);
        Assert.Equal("Unknown", Assert.Single(audit.LoggedEntries).UserName);
    }
}
