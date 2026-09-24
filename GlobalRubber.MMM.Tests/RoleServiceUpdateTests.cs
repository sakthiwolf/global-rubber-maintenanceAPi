using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// UpdateAsync tests for the real RoleService. Partial-class extension of
/// <see cref="RoleServiceTests"/> so it shares that file's fakes. Roles in <c>SampleRoles()</c>:
/// id 1 ADMIN (system role), id 2 MAINT_MANAGER (ordinary role).
/// </summary>
public partial class RoleServiceTests
{
    private static readonly DateTime FixedNow = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    private static UpdateRoleRequest Update(string code = "MAINT_MANAGER", string name = "Maintenance Manager", string? description = null) =>
        new() { RoleCode = code, RoleName = name, Description = description };

    [Fact]
    public async Task UpdateAsync_UpdatesCodeNameAndDescription_OfAnOrdinaryRole()
    {
        var repo = new FakeRoleRepository(SampleRoles());
        var service = CreateService(repo);

        var dto = await service.UpdateAsync(
            2, Update("MAINT_LEAD", "Maintenance Lead", "Leads maintenance"), actingUserId: 7, ipAddress: null, CancellationToken.None);

        Assert.Equal(2, dto.RoleId);
        Assert.Equal("MAINT_LEAD", dto.RoleCode);
        Assert.Equal("Maintenance Lead", dto.RoleName);
        Assert.Equal("Leads maintenance", dto.Description);

        var stored = repo.Stored(2);
        Assert.Equal("MAINT_LEAD", stored.RoleCode);
        Assert.Equal("Maintenance Lead", stored.RoleName);
        Assert.Equal("Leads maintenance", stored.Description);
    }

    [Fact]
    public async Task UpdateAsync_CanChangeOnlyTheRoleName()
    {
        var repo = new FakeRoleRepository(SampleRoles());

        var dto = await CreateService(repo).UpdateAsync(2, Update(name: "Maintenance Chief"), 7, null, CancellationToken.None);

        Assert.Equal("Maintenance Chief", dto.RoleName);
        Assert.Equal("MAINT_MANAGER", dto.RoleCode);
    }

    [Fact]
    public async Task UpdateAsync_CanChangeOnlyTheDescription_AndBlankBecomesNull()
    {
        var repo = new FakeRoleRepository(SampleRoles());
        var service = CreateService(repo);

        var withText = await service.UpdateAsync(2, Update(description: "  New text  "), 7, null, CancellationToken.None);
        Assert.Equal("New text", withText.Description);

        var blank = await service.UpdateAsync(2, Update(description: "   "), 7, null, CancellationToken.None);
        Assert.Null(blank.Description);
        Assert.Null(repo.Stored(2).Description);
    }

    [Fact]
    public async Task UpdateAsync_TrimsFields_AndUpperCasesRoleCode()
    {
        var repo = new FakeRoleRepository(SampleRoles());

        var dto = await CreateService(repo).UpdateAsync(
            2, Update("  maint_lead2  ", "  Maintenance Lead  ", "  desc  "), 7, null, CancellationToken.None);

        Assert.Equal("MAINT_LEAD2", dto.RoleCode);
        Assert.Equal("Maintenance Lead", dto.RoleName);
        Assert.Equal("desc", dto.Description);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("HAS SPACE")]
    [InlineData("HAS-DASH")]
    [InlineData("CAFÉ")]
    public async Task UpdateAsync_RejectsInvalidRoleCode_AndChangesNothing(string code)
    {
        var repo = new FakeRoleRepository(SampleRoles());
        var audit = new FakeAuditLogService();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService(repo, audit).UpdateAsync(2, Update(code: code), 7, null, CancellationToken.None));

        Assert.Contains(ex.Errors, e => e.StartsWith("RoleCode"));
        Assert.Equal(0, repo.UpdateCallCount);
        Assert.Empty(audit.LoggedEntries);
    }

    [Fact]
    public async Task UpdateAsync_RejectsRoleCodeOverMaxLength_ButAcceptsExactlyMax()
    {
        var repo = new FakeRoleRepository(SampleRoles());
        var service = CreateService(repo);

        var ok = await service.UpdateAsync(2, Update(code: new string('A', 30)), 7, null, CancellationToken.None);
        Assert.Equal(30, ok.RoleCode.Length);

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            service.UpdateAsync(2, Update(code: new string('B', 31)), 7, null, CancellationToken.None));
        Assert.Contains(ex.Errors, e => e.Contains("at most 30"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateAsync_RejectsMissingRoleName(string name)
    {
        var repo = new FakeRoleRepository(SampleRoles());
        var audit = new FakeAuditLogService();

        var ex = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService(repo, audit).UpdateAsync(2, Update(name: name), 7, null, CancellationToken.None));

        Assert.Contains("RoleName is required.", ex.Errors);
        Assert.Equal(0, repo.UpdateCallCount);
        Assert.Empty(audit.LoggedEntries);
    }

    [Fact]
    public async Task UpdateAsync_RejectsOverlongNameAndDescription_ReportingAllErrorsTogether()
    {
        var service = CreateService(SampleRoles());

        var ex = await Assert.ThrowsAsync<ValidationException>(() => service.UpdateAsync(
            2, Update(code: "", name: new string('n', 101), description: new string('d', 201)), 7, null, CancellationToken.None));

        Assert.Equal(3, ex.Errors.Count);
    }

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("admin")]   // normalized to upper case before the uniqueness check
    [InlineData("  Admin ")]
    public async Task UpdateAsync_RejectsRoleCodeBelongingToAnotherRole_WithConflict(string code)
    {
        var repo = new FakeRoleRepository(SampleRoles());
        var audit = new FakeAuditLogService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService(repo, audit).UpdateAsync(2, Update(code: code), 7, null, CancellationToken.None));

        Assert.Equal("MAINT_MANAGER", repo.Stored(2).RoleCode);
        Assert.Equal(0, repo.UpdateCallCount);
        Assert.Empty(audit.LoggedEntries);
    }

    [Theory]
    [InlineData("Administrator")]
    [InlineData("administrator")]
    [InlineData("  ADMINISTRATOR ")]
    public async Task UpdateAsync_RejectsRoleNameBelongingToAnotherRole_CaseInsensitively_WithConflict(string name)
    {
        var repo = new FakeRoleRepository(SampleRoles());
        var audit = new FakeAuditLogService();

        await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService(repo, audit).UpdateAsync(2, Update(name: name), 7, null, CancellationToken.None));

        Assert.Equal("Maintenance Manager", repo.Stored(2).RoleName);
        Assert.Equal(0, repo.UpdateCallCount);
        Assert.Empty(audit.LoggedEntries);
    }

    [Fact]
    public async Task UpdateAsync_DoesNotTreatTheRoleAsItsOwnDuplicate()
    {
        var repo = new FakeRoleRepository(SampleRoles());

        // Same code (different case) and same name (different case) as itself - not a conflict.
        var dto = await CreateService(repo).UpdateAsync(
            2, Update("maint_manager", "MAINTENANCE MANAGER", "now with a description"), 7, null, CancellationToken.None);

        Assert.Equal("MAINT_MANAGER", dto.RoleCode);
        Assert.Equal("MAINTENANCE MANAGER", dto.RoleName);
    }

    [Fact]
    public async Task UpdateAsync_ThrowsNotFound_WhenRoleDoesNotExist_AndAuditsNothing()
    {
        var repo = new FakeRoleRepository(SampleRoles());
        var audit = new FakeAuditLogService();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateService(repo, audit).UpdateAsync(999, Update(), 7, null, CancellationToken.None));

        Assert.Equal(0, repo.UpdateCallCount);
        Assert.Empty(audit.LoggedEntries);
    }

    // ---- system-role rules ----

    [Theory]
    [InlineData("SUPER_ADMIN")]
    [InlineData("super_admin")]
    public async Task UpdateAsync_RefusesToChangeTheRoleCodeOfASystemRole_WithConflict(string newCode)
    {
        var repo = new FakeRoleRepository(SampleRoles());
        var audit = new FakeAuditLogService();

        var ex = await Assert.ThrowsAsync<ConflictException>(() => CreateService(repo, audit).UpdateAsync(
            1, Update(newCode, "Administrator", "Full access to all modules"), 7, null, CancellationToken.None));

        Assert.Contains("system role", ex.Message);
        Assert.Equal("ADMIN", repo.Stored(1).RoleCode);
        Assert.Equal(0, repo.UpdateCallCount);
        Assert.Empty(audit.LoggedEntries);
    }

    [Fact]
    public async Task UpdateAsync_AllowsEditingNameAndDescriptionOfASystemRole_KeepingItASystemRole()
    {
        var repo = new FakeRoleRepository(SampleRoles());

        // The current code is sent back (any casing/whitespace of the SAME code is not a change).
        var dto = await CreateService(repo).UpdateAsync(
            1, Update("  admin ", "Super Administrator", "Everything"), 7, null, CancellationToken.None);

        Assert.Equal("ADMIN", dto.RoleCode);
        Assert.Equal("Super Administrator", dto.RoleName);
        Assert.Equal("Everything", dto.Description);
        Assert.True(dto.IsSystemRole);
        Assert.True(repo.Stored(1).IsSystemRole);
        Assert.True(repo.Stored(1).IsActive);
    }

    [Fact]
    public async Task UpdateAsync_NeverChangesIsSystemRoleOrIsActive_OrCreationFields()
    {
        var roles = SampleRoles();
        roles[1].IsActive = false; // an inactive ordinary role can still be edited - but stays inactive
        roles[1].CreatedBy = 3;
        roles[1].CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var repo = new FakeRoleRepository(roles);

        var dto = await CreateService(repo).UpdateAsync(2, Update(name: "Renamed"), 7, null, CancellationToken.None);

        Assert.False(dto.IsActive);
        Assert.False(dto.IsSystemRole);
        Assert.Equal(3, repo.Stored(2).CreatedBy);
        Assert.Equal(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), repo.Stored(2).CreatedAt);
    }

    // ---- UpdatedBy / UpdatedAt ----

    [Fact]
    public async Task UpdateAsync_SetsUpdatedByAndUpdatedAt()
    {
        var repo = new FakeRoleRepository(SampleRoles());

        await CreateService(repo).UpdateAsync(2, Update(name: "Renamed"), actingUserId: 7, null, CancellationToken.None);

        Assert.Equal(7, repo.Stored(2).UpdatedBy);
        Assert.Equal(FixedNow, repo.Stored(2).UpdatedAt);
    }

    // ---- concurrency ----

    [Fact]
    public async Task UpdateAsync_ThrowsConflict_WhenTheRoleWasModifiedAfterItWasLoaded_AndDoesNotOverwrite()
    {
        var repo = new FakeRoleRepository(SampleRoles());
        var audit = new FakeAuditLogService();
        // Another user saves between this request loading the role and saving it.
        repo.BeforeUpdate = roleId => repo.SimulateConcurrentModification(roleId);

        var ex = await Assert.ThrowsAsync<ConflictException>(() =>
            CreateService(repo, audit).UpdateAsync(2, Update(name: "My Late Edit"), 7, null, CancellationToken.None));

        Assert.Contains("modified by another user", ex.Message);
        Assert.Equal("Maintenance Manager", repo.Stored(2).RoleName); // the other user's version is intact
        Assert.Empty(audit.LoggedEntries);
    }

    // ---- audit ----

    [Fact]
    public async Task UpdateAsync_WritesExactlyOneRoleUpdatedAuditEntry_WithUserIpAndRoleDetails()
    {
        var audit = new FakeAuditLogService();
        var service = CreateService(SampleRoles(), audit);

        await service.UpdateAsync(
            2, Update("MAINT_LEAD", "Maintenance Lead", "desc"), actingUserId: 7, ipAddress: "10.0.0.5", CancellationToken.None);

        var entry = Assert.Single(audit.LoggedEntries);
        Assert.Equal(7, entry.UserId);
        Assert.Equal("Francis Xavier", entry.UserName);
        Assert.Equal("Security", entry.Module);
        Assert.Equal("RoleUpdated", entry.Action);
        Assert.Equal("Role", entry.EntityName);
        Assert.Equal(2, entry.EntityId);
        Assert.Equal("MAINT_LEAD", entry.RecordRef);
        Assert.Equal("10.0.0.5", entry.IpAddress);
        Assert.Contains("Francis Xavier", entry.Description);
        Assert.Contains("RoleCode 'MAINT_MANAGER' -> 'MAINT_LEAD'", entry.Description);
        Assert.Contains("RoleName", entry.Description);
        Assert.Contains("Description", entry.Description);
    }

    [Fact]
    public async Task UpdateAsync_AuditDescriptionListsOnlyWhatChanged()
    {
        var audit = new FakeAuditLogService();

        await CreateService(SampleRoles(), audit).UpdateAsync(
            2, Update(name: "Maintenance Manager", description: "New description"), 7, null, CancellationToken.None);

        var entry = Assert.Single(audit.LoggedEntries);
        Assert.Contains("Changed: Description.", entry.Description);
        Assert.DoesNotContain("RoleName", entry.Description);
        Assert.DoesNotContain("RoleCode", entry.Description);
    }

    [Fact]
    public async Task UpdateAsync_StillUpdatesAndAudits_WhenActingUserCannotBeResolved()
    {
        var audit = new FakeAuditLogService();

        await CreateService(SampleRoles(), audit).UpdateAsync(2, Update(name: "Renamed"), actingUserId: null, null, CancellationToken.None);

        var entry = Assert.Single(audit.LoggedEntries);
        Assert.Null(entry.UserId);
        Assert.Equal("Unknown", entry.UserName);
    }

    [Fact]
    public async Task UpdateAsync_Succeeds_EvenIfTheAuditDatabaseWriteFails()
    {
        var repo = new FakeRoleRepository(SampleRoles());
        // Real AuditLogService, whose repository throws on every insert.
        var service = CreateService(repo, AuditServiceWhoseDatabaseFails());

        var dto = await service.UpdateAsync(2, Update(name: "Renamed"), 7, null, CancellationToken.None);

        Assert.Equal("Renamed", dto.RoleName);
        Assert.Equal("Renamed", repo.Stored(2).RoleName);
    }

    [Fact]
    public async Task UpdateAsync_Succeeds_EvenIfTheAuditUserNameLookupFails()
    {
        var repo = new FakeRoleRepository(SampleRoles());
        var audit = new FakeAuditLogService();
        var service = CreateService(repo, audit, new FakeUserRepository(lookupFails: true));

        var dto = await service.UpdateAsync(2, Update(name: "Renamed"), 7, null, CancellationToken.None);

        Assert.Equal("Renamed", dto.RoleName);
        Assert.Equal("Unknown", Assert.Single(audit.LoggedEntries).UserName);
    }
}
