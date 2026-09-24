using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Unit tests against the real <see cref="PermissionService"/> with fake repositories - unlike
/// <see cref="RolePermissionControllerTests"/> (which fakes IPermissionService itself), these
/// exercise the actual matrix-building, upsert and validation logic.
/// </summary>
public class PermissionServiceTests
{
    private static Role AdminRole => new() { RoleId = 1, RoleCode = "ADMIN", RoleName = "Administrator", IsActive = true };

    private static List<Module> SampleModules() => new()
    {
        new Module { ModuleId = 1, ModuleCode = "DASHBOARD", ModuleName = "Dashboard", MenuGroup = "Dashboard", SortOrder = 1, IsActive = true },
        new Module { ModuleId = 2, ModuleCode = "MASTER_MACHINE", ModuleName = "Machine", MenuGroup = "Masters", SortOrder = 10, IsActive = true },
        new Module { ModuleId = 3, ModuleCode = "MASTER_MOLD", ModuleName = "Mold", MenuGroup = "Masters", SortOrder = 11, IsActive = false }, // inactive - excluded
    };

    [Fact]
    public async Task GetRolePermissionsAsync_ReturnsFullMatrix_WithMissingRowsAsAllFalse()
    {
        var repository = new FakePermissionRepository(SampleModules(), existingPermissions: new List<Permission>
        {
            new() { RoleId = 1, ModuleId = 1, CanView = true, CanExport = true },
            // No row at all for ModuleId 2 - should surface as an all-false entry, not an error.
        });

        var service = new PermissionService(repository, new FakeRoleRepository(AdminRole), new FakeDateTimeProvider());

        var matrix = await service.GetRolePermissionsAsync(1, CancellationToken.None);

        Assert.Equal("ADMIN", matrix.RoleCode);
        Assert.Equal(2, matrix.Permissions.Count); // only the 2 active modules, inactive Mold excluded

        var dashboard = matrix.Permissions.Single(p => p.ModuleCode == "DASHBOARD");
        Assert.True(dashboard.CanView);
        Assert.True(dashboard.CanExport);

        var machine = matrix.Permissions.Single(p => p.ModuleCode == "MASTER_MACHINE");
        Assert.False(machine.CanView);
        Assert.False(machine.CanAdd);
    }

    [Fact]
    public async Task GetRolePermissionsAsync_ThrowsNotFoundException_WhenRoleDoesNotExist()
    {
        var service = new PermissionService(
            new FakePermissionRepository(SampleModules(), new List<Permission>()),
            new FakeRoleRepository(role: null),
            new FakeDateTimeProvider());

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetRolePermissionsAsync(999, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_UpdatesExistingRow_AndLeavesOthersUntouched()
    {
        var repository = new FakePermissionRepository(SampleModules(), existingPermissions: new List<Permission>
        {
            new() { RoleId = 1, ModuleId = 1, CanView = true, CanExport = true },
        });
        var service = new PermissionService(repository, new FakeRoleRepository(AdminRole), new FakeDateTimeProvider());

        var matrix = await service.UpdateRolePermissionsAsync(1, new UpdateRolePermissionsRequest
        {
            Permissions = new[]
            {
                new ModulePermissionUpdateDto { ModuleId = 2, CanView = true, CanAdd = true, CanEdit = true },
            },
        }, CancellationToken.None);

        var machine = matrix.Permissions.Single(p => p.ModuleCode == "MASTER_MACHINE");
        Assert.True(machine.CanView);
        Assert.True(machine.CanAdd);
        Assert.True(machine.CanEdit);

        // Module 1 was not in the request - untouched, still exactly as it was.
        var dashboard = matrix.Permissions.Single(p => p.ModuleCode == "DASHBOARD");
        Assert.True(dashboard.CanView);
        Assert.True(dashboard.CanExport);
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_InsertsNewRow_WhenNoPermissionRowPreviouslyExisted()
    {
        var repository = new FakePermissionRepository(SampleModules(), existingPermissions: new List<Permission>());
        var service = new PermissionService(repository, new FakeRoleRepository(AdminRole), new FakeDateTimeProvider());

        var matrix = await service.UpdateRolePermissionsAsync(1, new UpdateRolePermissionsRequest
        {
            Permissions = new[]
            {
                new ModulePermissionUpdateDto { ModuleId = 2, CanView = true, CanApprove = true },
            },
        }, CancellationToken.None);

        var machine = matrix.Permissions.Single(p => p.ModuleCode == "MASTER_MACHINE");
        Assert.True(machine.CanView);
        Assert.True(machine.CanApprove);
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_ThrowsValidationException_ForDuplicateModuleId()
    {
        var repository = new FakePermissionRepository(SampleModules(), new List<Permission>());
        var service = new PermissionService(repository, new FakeRoleRepository(AdminRole), new FakeDateTimeProvider());

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateRolePermissionsAsync(1, new UpdateRolePermissionsRequest
        {
            Permissions = new[]
            {
                new ModulePermissionUpdateDto { ModuleId = 2, CanView = true },
                new ModulePermissionUpdateDto { ModuleId = 2, CanView = false },
            },
        }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_ThrowsValidationException_ForUnknownModuleId()
    {
        var repository = new FakePermissionRepository(SampleModules(), new List<Permission>());
        var service = new PermissionService(repository, new FakeRoleRepository(AdminRole), new FakeDateTimeProvider());

        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateRolePermissionsAsync(1, new UpdateRolePermissionsRequest
        {
            Permissions = new[] { new ModulePermissionUpdateDto { ModuleId = 999, CanView = true } },
        }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_ThrowsValidationException_ForInactiveModuleId()
    {
        var repository = new FakePermissionRepository(SampleModules(), new List<Permission>());
        var service = new PermissionService(repository, new FakeRoleRepository(AdminRole), new FakeDateTimeProvider());

        // ModuleId 3 (Mold) exists but is inactive - rejected the same as a genuinely unknown id.
        await Assert.ThrowsAsync<ValidationException>(() => service.UpdateRolePermissionsAsync(1, new UpdateRolePermissionsRequest
        {
            Permissions = new[] { new ModulePermissionUpdateDto { ModuleId = 3, CanView = true } },
        }, CancellationToken.None));
    }

    [Fact]
    public async Task UpdateRolePermissionsAsync_ThrowsNotFoundException_WhenRoleDoesNotExist()
    {
        var service = new PermissionService(
            new FakePermissionRepository(SampleModules(), new List<Permission>()),
            new FakeRoleRepository(role: null),
            new FakeDateTimeProvider());

        await Assert.ThrowsAsync<NotFoundException>(() => service.UpdateRolePermissionsAsync(999, new UpdateRolePermissionsRequest(), CancellationToken.None));
    }

    private sealed class FakeDateTimeProvider : IDateTimeProvider
    {
        public DateTime UtcNow => new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }

    private sealed class FakeRoleRepository : IRoleRepository
    {
        private readonly Role? _role;

        public FakeRoleRepository(Role? role)
        {
            _role = role;
        }

        public Task<(IReadOnlyList<Role> Items, int TotalCount)> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by PermissionServiceTests.");

        public Task<Role?> GetByIdAsync(int roleId, CancellationToken cancellationToken) =>
            Task.FromResult(_role is not null && _role.RoleId == roleId ? _role : null);
    }

    /// <summary>Mirrors the real PermissionRepository's upsert semantics in memory.</summary>
    private sealed class FakePermissionRepository : IPermissionRepository
    {
        private readonly List<Module> _modules;
        private readonly Dictionary<(int RoleId, int ModuleId), Permission> _permissions;

        public FakePermissionRepository(List<Module> modules, List<Permission> existingPermissions)
        {
            _modules = modules;
            _permissions = existingPermissions.ToDictionary(p => (p.RoleId, p.ModuleId));
        }

        public Task<IReadOnlyList<Module>> GetActiveModulesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Module>>(_modules.Where(m => m.IsActive).OrderBy(m => m.SortOrder).ToList());

        public Task<IReadOnlyList<Permission>> GetByRoleIdAsync(int roleId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Permission>>(_permissions.Values.Where(p => p.RoleId == roleId).ToList());

        public Task<PermissionActionFlags?> GetPermissionFlagsAsync(
            string roleCode, string moduleCode, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by PermissionServiceTests - see PermissionAuthorizationServiceTests.");

        public Task SaveRolePermissionsAsync(int roleId, IReadOnlyList<Permission> permissions, CancellationToken cancellationToken)
        {
            foreach (var incoming in permissions)
            {
                var key = (roleId, incoming.ModuleId);
                if (_permissions.TryGetValue(key, out var existing))
                {
                    existing.CanView = incoming.CanView;
                    existing.CanAdd = incoming.CanAdd;
                    existing.CanEdit = incoming.CanEdit;
                    existing.CanDelete = incoming.CanDelete;
                    existing.CanApprove = incoming.CanApprove;
                    existing.CanExport = incoming.CanExport;
                    existing.UpdatedAt = incoming.UpdatedAt;
                }
                else
                {
                    incoming.RoleId = roleId;
                    _permissions[key] = incoming;
                }
            }

            return Task.CompletedTask;
        }
    }
}
