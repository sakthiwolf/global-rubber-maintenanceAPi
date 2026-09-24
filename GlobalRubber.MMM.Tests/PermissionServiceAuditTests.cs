using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// PermissionsUpdated auditing (audit_log + one audit_log_detail row per changed flag), UpdatedBy,
/// GetMyPermissionsAsync, and the "audit never fails the business operation" rule, against the real
/// <see cref="PermissionService"/>. Partial-class extension of <see cref="PermissionServiceTests"/>,
/// sharing its fakes and sample data (Admin role id 1; modules: 1 DASHBOARD, 2 MASTER_MACHINE, 3 inactive MASTER_MOLD).
/// </summary>
public partial class PermissionServiceTests
{
    private static PermissionService CreateService(
        IPermissionRepository permissionRepository,
        IRoleRepository roleRepository,
        IAuditLogService? auditLogService = null,
        IUserRepository? userRepository = null) =>
        new(permissionRepository,
            roleRepository,
            userRepository ?? new PermissionTestUserRepository(),
            new FakeDateTimeProvider(),
            auditLogService ?? new PermissionTestAuditLog(),
            NullLogger<PermissionService>.Instance);

    private static UpdateRolePermissionsRequest Request(params ModulePermissionUpdateDto[] updates) =>
        new() { Permissions = updates };

    private static FakePermissionRepository RepoWithDashboardViewExport() =>
        new(SampleModules(), new List<Permission>
        {
            new() { RoleId = 1, ModuleId = 1, CanView = true, CanExport = true },
            // no row at all for MASTER_MACHINE
        });

    // ---- PermissionsUpdated audit ----

    [Fact]
    public async Task UpdateRolePermissionsAsync_WritesOnePermissionsUpdatedEntry_WithUserIpAndRoleDetails()
    {
        var audit = new PermissionTestAuditLog();
        var service = CreateService(RepoWithDashboardViewExport(), new FakeRoleRepository(AdminRole), audit);

        await service.UpdateRolePermissionsAsync(
            1, Request(new ModulePermissionUpdateDto { ModuleId = 2, CanView = true }), 7, "10.0.0.5", CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Equal(7, entry.UserId);
        Assert.Equal("Francis Xavier", entry.UserName);
        Assert.Equal("Security", entry.Module);
        Assert.Equal("PermissionsUpdated", entry.Action);
        Assert.Equal("Role", entry.EntityName);
        Assert.Equal(1, entry.EntityId);
        Assert.Equal("ADMIN", entry.RecordRef);
        Assert.Equal("10.0.0.5", entry.IpAddress);
        Assert.Contains("Administrator", entry.Description);
        Assert.Contains("1 change(s) in 1 module(s)", entry.Description);
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_AuditsOneDetailPerChangedFlag_WithOldAndNewValues()
    {
        var audit = new PermissionTestAuditLog();
        var service = CreateService(RepoWithDashboardViewExport(), new FakeRoleRepository(AdminRole), audit);

        // Dashboard was View+Export. View unchanged (true), Add false->true, Export true->false.
        await service.UpdateRolePermissionsAsync(1, Request(
            new ModulePermissionUpdateDto { ModuleId = 1, CanView = true, CanAdd = true, CanExport = false }),
            7, null, CancellationToken.None);

        var details = Assert.Single(audit.Entries).Details;
        Assert.Equal(2, details.Count);
        Assert.Contains(new AuditLogDetailEntry("DASHBOARD.can_add", "false", "true"), details);
        Assert.Contains(new AuditLogDetailEntry("DASHBOARD.can_export", "true", "false"), details);
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_UnchangedFlagsProduceNoDetailRows()
    {
        var audit = new PermissionTestAuditLog();
        var service = CreateService(RepoWithDashboardViewExport(), new FakeRoleRepository(AdminRole), audit);

        // Exactly what is already stored.
        await service.UpdateRolePermissionsAsync(1, Request(
            new ModulePermissionUpdateDto { ModuleId = 1, CanView = true, CanExport = true }),
            7, null, CancellationToken.None);

        var entry = Assert.Single(audit.Entries);
        Assert.Empty(entry.Details);
        Assert.Contains("no permission values changed", entry.Description);
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_TreatsAMissingRowAsAllFalse_WhenDiffing()
    {
        var audit = new PermissionTestAuditLog();
        var service = CreateService(RepoWithDashboardViewExport(), new FakeRoleRepository(AdminRole), audit);

        // MASTER_MACHINE has no row. All-false is "no change" (missing row already means no access)...
        await service.UpdateRolePermissionsAsync(1, Request(new ModulePermissionUpdateDto { ModuleId = 2 }), 7, null, CancellationToken.None);
        Assert.Empty(audit.Entries[0].Details);

        // ...and granting View is exactly one change from false.
        await service.UpdateRolePermissionsAsync(1, Request(new ModulePermissionUpdateDto { ModuleId = 2, CanView = true }), 7, null, CancellationToken.None);
        var detail = Assert.Single(audit.Entries[1].Details);
        Assert.Equal(new AuditLogDetailEntry("MASTER_MACHINE.can_view", "false", "true"), detail);
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_AllSixFlagsAreSavedAndAudited()
    {
        var audit = new PermissionTestAuditLog();
        var repo = new FakePermissionRepository(SampleModules(), new List<Permission>());
        var service = CreateService(repo, new FakeRoleRepository(AdminRole), audit);

        var matrix = await service.UpdateRolePermissionsAsync(1, Request(new ModulePermissionUpdateDto
        {
            ModuleId = 2, CanView = true, CanAdd = true, CanEdit = true, CanDelete = true, CanApprove = true, CanExport = true,
        }), 7, null, CancellationToken.None);

        var machine = matrix.Permissions.Single(p => p.ModuleCode == "MASTER_MACHINE");
        Assert.True(machine.CanView && machine.CanAdd && machine.CanEdit && machine.CanDelete && machine.CanApprove && machine.CanExport);

        var fields = Assert.Single(audit.Entries).Details.Select(d => d.FieldName).ToList();
        Assert.Equal(
            new[]
            {
                "MASTER_MACHINE.can_view", "MASTER_MACHINE.can_add", "MASTER_MACHINE.can_edit",
                "MASTER_MACHINE.can_delete", "MASTER_MACHINE.can_approve", "MASTER_MACHINE.can_export",
            },
            fields);
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_DetailRowsAreFlatFieldValuePairs_NotSerializedMatrices()
    {
        var audit = new PermissionTestAuditLog();
        var service = CreateService(RepoWithDashboardViewExport(), new FakeRoleRepository(AdminRole), audit);

        await service.UpdateRolePermissionsAsync(1, Request(
            new ModulePermissionUpdateDto { ModuleId = 2, CanView = true, CanAdd = true },
            new ModulePermissionUpdateDto { ModuleId = 1, CanView = false }), 7, null, CancellationToken.None);

        var details = Assert.Single(audit.Entries).Details;
        Assert.All(details, d =>
        {
            Assert.Matches("^[A-Z_]+\\.can_(view|add|edit|delete|approve|export)$", d.FieldName);
            Assert.Contains(d.OldValue, new[] { "true", "false" });
            Assert.Contains(d.NewValue, new[] { "true", "false" });
        });
        // Dashboard was View+Export and the request turns both off (View explicitly, Export by omission = false);
        // Machine had no row, so View and Add are new grants. Deterministic order: by module code, then flag order.
        Assert.Equal(
            new[] { "DASHBOARD.can_view", "DASHBOARD.can_export", "MASTER_MACHINE.can_view", "MASTER_MACHINE.can_add" },
            details.Select(d => d.FieldName).ToArray());
    }

    // ---- persistence / UpdatedBy ----

    [Fact]
    public async Task UpdateRolePermissionsAsync_PersistsTheChange_AndItIsReadBackByGet()
    {
        var repo = RepoWithDashboardViewExport();
        var service = CreateService(repo, new FakeRoleRepository(AdminRole));

        await service.UpdateRolePermissionsAsync(1, Request(
            new ModulePermissionUpdateDto { ModuleId = 2, CanView = true, CanEdit = true }), 7, null, CancellationToken.None);

        var matrix = await service.GetRolePermissionsAsync(1, CancellationToken.None);
        var machine = matrix.Permissions.Single(p => p.ModuleCode == "MASTER_MACHINE");
        Assert.True(machine.CanView);
        Assert.True(machine.CanEdit);
        Assert.False(machine.CanAdd);
        // Untouched module keeps its stored values.
        Assert.True(matrix.Permissions.Single(p => p.ModuleCode == "DASHBOARD").CanExport);
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_SetsCreatedByAndUpdatedByOnTheSavedRows()
    {
        var repo = RepoWithDashboardViewExport();
        var service = CreateService(repo, new FakeRoleRepository(AdminRole));

        await service.UpdateRolePermissionsAsync(1, Request(new ModulePermissionUpdateDto { ModuleId = 2, CanView = true }), 7, null, CancellationToken.None);

        var saved = (await repo.GetByRoleIdAsync(1, CancellationToken.None)).Single(p => p.ModuleId == 2);
        Assert.Equal(7, saved.UpdatedBy);
        Assert.Equal(7, saved.CreatedBy);
    }

    // ---- failures never write a success audit entry ----

    [Fact]
    public async Task UpdateRolePermissionsAsync_WritesNoAuditEntry_WhenTheRequestIsRejected()
    {
        var audit = new PermissionTestAuditLog();
        var service = CreateService(RepoWithDashboardViewExport(), new FakeRoleRepository(AdminRole), audit);

        // unknown module, inactive module (id 3), duplicate module id
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateRolePermissionsAsync(
            1, Request(new ModulePermissionUpdateDto { ModuleId = 999, CanView = true }), 7, null, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateRolePermissionsAsync(
            1, Request(new ModulePermissionUpdateDto { ModuleId = 3, CanView = true }), 7, null, CancellationToken.None));
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateRolePermissionsAsync(
            1, Request(new ModulePermissionUpdateDto { ModuleId = 2 }, new ModulePermissionUpdateDto { ModuleId = 2 }), 7, null, CancellationToken.None));
        // unknown role
        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateRolePermissionsAsync(
            404, Request(new ModulePermissionUpdateDto { ModuleId = 2 }), 7, null, CancellationToken.None));

        Assert.Empty(audit.Entries);
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_WritesNoAuditEntry_WhenTheSaveFails()
    {
        var audit = new PermissionTestAuditLog();
        var failingSave = new SaveFailsPermissionRepository(RepoWithDashboardViewExport());
        var service = CreateService(failingSave, new FakeRoleRepository(AdminRole), audit);

        await Assert.ThrowsAsync<ConflictException>(() => service.UpdateRolePermissionsAsync(
            1, Request(new ModulePermissionUpdateDto { ModuleId = 2, CanView = true }), 7, null, CancellationToken.None));

        Assert.Empty(audit.Entries); // audited only AFTER a successful save
    }

    // ---- audit failure never fails the business operation ----

    [Fact]
    public async Task UpdateRolePermissionsAsync_Succeeds_EvenIfTheAuditDatabaseWriteFails()
    {
        var repo = RepoWithDashboardViewExport();
        // Real AuditLogService whose repository throws on every insert.
        var failingAudit = new AuditLogService(
            new ThrowingPermissionAuditRepository(), new FakeDateTimeProvider(), NullLogger<AuditLogService>.Instance);
        var service = CreateService(repo, new FakeRoleRepository(AdminRole), failingAudit);

        var matrix = await service.UpdateRolePermissionsAsync(
            1, Request(new ModulePermissionUpdateDto { ModuleId = 2, CanView = true }), 7, null, CancellationToken.None);

        Assert.True(matrix.Permissions.Single(p => p.ModuleCode == "MASTER_MACHINE").CanView);
        Assert.True((await repo.GetByRoleIdAsync(1, CancellationToken.None)).Single(p => p.ModuleId == 2).CanView);
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_Succeeds_EvenIfTheAuditUserNameLookupFails()
    {
        var audit = new PermissionTestAuditLog();
        var service = CreateService(
            RepoWithDashboardViewExport(), new FakeRoleRepository(AdminRole), audit, new PermissionTestUserRepository(lookupFails: true));

        var matrix = await service.UpdateRolePermissionsAsync(
            1, Request(new ModulePermissionUpdateDto { ModuleId = 2, CanView = true }), 7, null, CancellationToken.None);

        Assert.True(matrix.Permissions.Single(p => p.ModuleCode == "MASTER_MACHINE").CanView);
        Assert.Equal("Unknown", Assert.Single(audit.Entries).UserName);
    }

    // ---- the caller's own permissions ----

    [Fact]
    public async Task GetMyPermissionsAsync_ReturnsTheMatrixOfTheUsersOwnRole()
    {
        var service = CreateService(RepoWithDashboardViewExport(), new FakeRoleRepository(AdminRole));

        var matrix = await service.GetMyPermissionsAsync(userId: 7, CancellationToken.None); // user 7 has role 1

        Assert.Equal(1, matrix.RoleId);
        Assert.Equal("ADMIN", matrix.RoleCode);
        Assert.True(matrix.Permissions.Single(p => p.ModuleCode == "DASHBOARD").CanView);
        Assert.False(matrix.Permissions.Single(p => p.ModuleCode == "MASTER_MACHINE").CanView); // missing row -> false
    }

    [Fact]
    public async Task GetMyPermissionsAsync_ThrowsUnauthorized_WhenTheTokensUserNoLongerExists()
    {
        var service = CreateService(RepoWithDashboardViewExport(), new FakeRoleRepository(AdminRole));

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.GetMyPermissionsAsync(999, CancellationToken.None));
    }

    // ---- fakes ----

    private sealed class PermissionTestAuditLog : IAuditLogService
    {
        public List<AuditLogEntry> Entries { get; } = new();

        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingPermissionAuditRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit DB failure.");
    }

    /// <summary>User 7 "Francis Xavier" with role 1 - enough for the audit user name and for GetMyPermissionsAsync.</summary>
    private sealed class PermissionTestUserRepository : IUserRepository
    {
        private readonly bool _lookupFails;

        public PermissionTestUserRepository(bool lookupFails = false)
        {
            _lookupFails = lookupFails;
        }

        public Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken) =>
            _lookupFails
                ? throw new InvalidOperationException("Simulated user lookup failure.")
                : Task.FromResult<User?>(userId == 7 ? new User { UserId = 7, UserName = "Francis Xavier", RoleId = 1 } : null);

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<User?> GetByLoginIdForAuthenticationAsync(string loginId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateLastLoginAtAsync(int userId, DateTime lastLoginAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> AnyActiveByRoleIdAsync(int roleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> ExistsByLoginIdAsync(string loginId, int? excludeUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed here.");

        public Task<User> AddAsync(User user, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed here.");

        public Task<User> UpdateAsync(User user, byte[] originalRowVersion, bool passwordChanged, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed here.");
    }

    /// <summary>Delegates reads to a real fake, but the save fails like a lost concurrency race.</summary>
    private sealed class SaveFailsPermissionRepository : IPermissionRepository
    {
        private readonly IPermissionRepository _inner;

        public SaveFailsPermissionRepository(IPermissionRepository inner)
        {
            _inner = inner;
        }

        public Task<PermissionActionFlags?> GetPermissionFlagsAsync(string roleCode, string moduleCode, CancellationToken cancellationToken) =>
            _inner.GetPermissionFlagsAsync(roleCode, moduleCode, cancellationToken);

        public Task<IReadOnlyList<Module>> GetActiveModulesAsync(CancellationToken cancellationToken) =>
            _inner.GetActiveModulesAsync(cancellationToken);

        public Task<IReadOnlyList<Permission>> GetByRoleIdAsync(int roleId, CancellationToken cancellationToken) =>
            _inner.GetByRoleIdAsync(roleId, cancellationToken);

        public Task SaveRolePermissionsAsync(int roleId, IReadOnlyList<Permission> permissions, CancellationToken cancellationToken) =>
            throw new ConflictException("The permissions were modified by another user while saving. Reload and try again.");
    }
}
