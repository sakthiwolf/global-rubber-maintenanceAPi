using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// POST/PUT /api/v1/roles through the real HTTP pipeline (JWT auth, [RequirePermission],
/// GlobalExceptionHandler, model binding) with the REAL RoleService and PermissionService.
/// The update tests live in RoleEndpointUpdateTests.cs (same partial class, same harness).
/// Only the persistence edge is faked - in-memory repositories - because the real
/// repositories need a live SQL Server this environment does not have. The final test walks the
/// whole feature flow: create role -> load its (all-unchecked) permission matrix -> save
/// permissions -> reload and confirm they persisted.
/// </summary>
public partial class RoleEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public RoleEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record Harness(
        HttpClient Client,
        InMemoryRoleRepository Roles,
        InMemoryPermissionRepository Permissions,
        RecordingAuditLogService Audit,
        RecordingPermissionAuthorizationService Authorization,
        StubUserRepository Users);

    private Harness CreateHarness(
        bool permissionGranted = true, bool authenticate = true, string tokenRoleCode = "ADMIN", int tokenUserId = 1)
    {
        var roles = new InMemoryRoleRepository(
            new Role { RoleId = 1, RoleCode = "ADMIN", RoleName = "Administrator", IsSystemRole = true, IsActive = true },
            new Role { RoleId = 2, RoleCode = "MAINT_MANAGER", RoleName = "Maintenance Manager", IsActive = true });
        var permissions = new InMemoryPermissionRepository(
            new Module { ModuleId = 10, ModuleCode = ModuleCodes.MasterMachine, ModuleName = "Machine", MenuGroup = "Masters", SortOrder = 1 },
            new Module { ModuleId = 11, ModuleCode = ModuleCodes.MasterMold, ModuleName = "Mold", MenuGroup = "Masters", SortOrder = 2 });
        var audit = new RecordingAuditLogService();
        var authorization = new RecordingPermissionAuthorizationService(permissionGranted);
        var users = new StubUserRepository();

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRoleRepository>();
                services.AddSingleton<IRoleRepository>(roles);
                services.RemoveAll<IPermissionRepository>();
                services.AddSingleton<IPermissionRepository>(permissions);
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(users);
                services.RemoveAll<IAuditLogService>();
                services.AddSingleton<IAuditLogService>(audit);
                services.RemoveAll<IPermissionAuthorizationService>();
                services.AddSingleton<IPermissionAuthorizationService>(authorization);
            });
        });

        var client = factory.CreateClient();

        if (authenticate)
        {
            TestAuth.Authenticate(client, factory, tokenRoleCode, tokenUserId);
        }

        return new Harness(client, roles, permissions, audit, authorization, users);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private const string ValidBody =
        """{"roleCode":"MAINT_SUPERVISOR","roleName":"Maintenance Supervisor","description":"Maintenance supervisor role","isActive":true}""";

    [Fact]
    public async Task Post_Returns201_WithRoleDto_AndLocationHeader()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/roles", Json(ValidBody));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.True(root.GetProperty("success").GetBoolean());

        var data = root.GetProperty("data");
        Assert.True(data.GetProperty("roleId").GetInt32() > 0);
        Assert.Equal("MAINT_SUPERVISOR", data.GetProperty("roleCode").GetString());
        Assert.Equal("Maintenance Supervisor", data.GetProperty("roleName").GetString());
        Assert.Equal("Maintenance supervisor role", data.GetProperty("description").GetString());
        Assert.False(data.GetProperty("isSystemRole").GetBoolean());
        Assert.True(data.GetProperty("isActive").GetBoolean());
        Assert.False(data.TryGetProperty("users", out _));
        Assert.False(data.TryGetProperty("permissions", out _));
    }

    [Fact]
    public async Task Post_IgnoresClientSuppliedIsSystemRole()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/roles", Json(
            """{"roleCode":"SNEAKY","roleName":"Sneaky","isSystemRole":true}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.False(h.Roles.Single("SNEAKY").IsSystemRole);
    }

    [Fact]
    public async Task Post_RequiresAdminRolesAddPermission()
    {
        var h = CreateHarness();

        await h.Client.PostAsync("/api/v1/roles", Json(ValidBody));

        var check = Assert.Single(h.Authorization.Checks);
        Assert.Equal("ADMIN", check.RoleCode);
        Assert.Equal(ModuleCodes.AdminRoles, check.ModuleCode);
        Assert.Equal(PermissionAction.Add, check.Action);
    }

    [Fact]
    public async Task Post_Returns401_WithoutToken_AndCreatesNothing()
    {
        var h = CreateHarness(authenticate: false);

        var response = await h.Client.PostAsync("/api/v1/roles", Json(ValidBody));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(2, h.Roles.Count);
    }

    [Fact]
    public async Task Post_Returns403_WhenPermissionDenied_AndCreatesNothing()
    {
        var h = CreateHarness(permissionGranted: false);

        var response = await h.Client.PostAsync("/api/v1/roles", Json(ValidBody));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(2, h.Roles.Count);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Post_Returns409_ForDuplicateRoleCode_WithoutLeakingDatabaseDetails()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/roles", Json(
            """{"roleCode":"maint_manager","roleName":"Something Different"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("MAINT_MANAGER", document.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("UQ_role_master", body);
        Assert.DoesNotContain("SqlException", body);
        Assert.Equal(2, h.Roles.Count);
    }

    [Fact]
    public async Task Post_Returns409_ForDuplicateRoleName_IgnoringCase()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/roles", Json(
            """{"roleCode":"BRAND_NEW","roleName":"MAINTENANCE MANAGER"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(2, h.Roles.Count);
    }

    [Fact]
    public async Task Post_Returns400_WithErrorList_ForMissingRoleName()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/roles", Json("""{"roleCode":"OK_CODE","roleName":"  "}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        var errors = document.RootElement.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("RoleName is required.", errors);
        Assert.Equal(2, h.Roles.Count);
    }

    [Fact]
    public async Task Post_WritesRoleCreatedAuditEntry_ForTheAuthenticatedUser()
    {
        var h = CreateHarness();

        await h.Client.PostAsync("/api/v1/roles", Json(ValidBody));

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal("RoleCreated", entry.Action);
        Assert.Equal(1, entry.UserId); // the JWT "sub" claim, read by the controller
        Assert.Equal("Test Admin", entry.UserName);
        Assert.Equal("MAINT_SUPERVISOR", entry.RecordRef);
    }

    [Fact]
    public async Task Post_ThenGetById_ReturnsTheNewRole()
    {
        var h = CreateHarness();

        var created = await h.Client.PostAsync("/api/v1/roles", Json(ValidBody));
        var location = created.Headers.Location!;

        var fetched = await h.Client.GetAsync(location);

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        using var document = JsonDocument.Parse(await fetched.Content.ReadAsStringAsync());
        Assert.Equal("MAINT_SUPERVISOR", document.RootElement.GetProperty("data").GetProperty("roleCode").GetString());
    }

    [Fact]
    public async Task NewRole_StartsWithNoPermissions_ThenSavedPermissionsPersist_AndInvalidModuleIsRejected()
    {
        var h = CreateHarness();

        var created = await h.Client.PostAsync("/api/v1/roles", Json(ValidBody));
        using var createdDoc = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        var roleId = createdDoc.RootElement.GetProperty("data").GetProperty("roleId").GetInt32();

        // Creating a role must not invent permission rows - and the matrix shows every active
        // module with every action unchecked.
        Assert.Equal(0, h.Permissions.RowCountForRole(roleId));
        var initial = await GetMatrixAsync(h.Client, roleId);
        Assert.Equal(2, initial.GetArrayLength());
        foreach (var module in initial.EnumerateArray())
        {
            foreach (var action in new[] { "canView", "canAdd", "canEdit", "canDelete", "canApprove", "canExport" })
            {
                Assert.False(module.GetProperty(action).GetBoolean());
            }
        }

        // An unknown module id is rejected and nothing is saved.
        var invalid = await h.Client.PutAsJsonAsync($"/api/v1/roles/{roleId}/permissions", new UpdateRolePermissionsRequest
        {
            Permissions = new[] { new ModulePermissionUpdateDto { ModuleId = 999, CanView = true } },
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Equal(0, h.Permissions.RowCountForRole(roleId));

        // Save Machine: View/Add/Edit/Export; Mold: View/Export (the example from the feature request).
        var saved = await h.Client.PutAsJsonAsync($"/api/v1/roles/{roleId}/permissions", new UpdateRolePermissionsRequest
        {
            Permissions = new[]
            {
                new ModulePermissionUpdateDto { ModuleId = 10, CanView = true, CanAdd = true, CanEdit = true, CanExport = true },
                new ModulePermissionUpdateDto { ModuleId = 11, CanView = true, CanExport = true },
            },
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);

        // Persisted: a fresh GET returns exactly what was saved.
        var reloaded = await GetMatrixAsync(h.Client, roleId);
        var machine = reloaded.EnumerateArray().Single(m => m.GetProperty("moduleId").GetInt32() == 10);
        var mold = reloaded.EnumerateArray().Single(m => m.GetProperty("moduleId").GetInt32() == 11);

        Assert.True(machine.GetProperty("canView").GetBoolean());
        Assert.True(machine.GetProperty("canAdd").GetBoolean());
        Assert.True(machine.GetProperty("canEdit").GetBoolean());
        Assert.False(machine.GetProperty("canDelete").GetBoolean());
        Assert.False(machine.GetProperty("canApprove").GetBoolean());
        Assert.True(machine.GetProperty("canExport").GetBoolean());

        Assert.True(mold.GetProperty("canView").GetBoolean());
        Assert.False(mold.GetProperty("canAdd").GetBoolean());
        Assert.True(mold.GetProperty("canExport").GetBoolean());

        // Exactly the two saved rows exist - nothing else was invented.
        Assert.Equal(2, h.Permissions.RowCountForRole(roleId));
    }

    private static async Task<JsonElement> GetMatrixAsync(HttpClient client, int roleId)
    {
        var response = await client.GetAsync($"/api/v1/roles/{roleId}/permissions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("data").GetProperty("permissions").Clone();
    }

    // ---- in-memory fakes (persistence edge only) ----

    private sealed class InMemoryRoleRepository : IRoleRepository
    {
        private readonly List<Role> _roles;

        public InMemoryRoleRepository(params Role[] roles)
        {
            _roles = roles.ToList();
        }

        public int Count => _roles.Count;

        /// <summary>When true, UpdateAsync/DeactivateAsync fail the way the real ones do when row_version no longer matches.</summary>
        public bool ConflictOnUpdate { get; set; }

        public Role Single(string roleCode) => _roles.Single(r => r.RoleCode == roleCode);

        public Role ById(int roleId) => _roles.Single(r => r.RoleId == roleId);

        public Task<(IReadOnlyList<Role> Items, int TotalCount)> GetAllAsync(
            PaginationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<(IReadOnlyList<Role>, int)>((_roles.ToList(), _roles.Count));

        // A detached copy, like the real repository's AsNoTracking read.
        public Task<Role?> GetByIdAsync(int roleId, CancellationToken cancellationToken)
        {
            var stored = _roles.FirstOrDefault(r => r.RoleId == roleId);

            return Task.FromResult(stored is null ? null : new Role
            {
                RoleId = stored.RoleId,
                RoleCode = stored.RoleCode,
                RoleName = stored.RoleName,
                Description = stored.Description,
                IsSystemRole = stored.IsSystemRole,
                IsActive = stored.IsActive,
                CreatedAt = stored.CreatedAt,
                CreatedBy = stored.CreatedBy,
                UpdatedAt = stored.UpdatedAt,
                UpdatedBy = stored.UpdatedBy,
                RowVersion = stored.RowVersion,
            });
        }

        public Task<bool> ExistsByCodeAsync(string roleCode, int? excludeRoleId, CancellationToken cancellationToken) =>
            Task.FromResult(_roles.Any(r => r.RoleCode == roleCode && r.RoleId != excludeRoleId));

        public Task<bool> ExistsByNameAsync(string roleName, int? excludeRoleId, CancellationToken cancellationToken) =>
            Task.FromResult(_roles.Any(r =>
                string.Equals(r.RoleName, roleName, StringComparison.OrdinalIgnoreCase) && r.RoleId != excludeRoleId));

        public Task<Role> AddAsync(Role role, CancellationToken cancellationToken)
        {
            role.RoleId = _roles.Max(r => r.RoleId) + 1;
            _roles.Add(role);
            return Task.FromResult(role);
        }

        public Task<Role> DeactivateAsync(Role role, CancellationToken cancellationToken)
        {
            if (ConflictOnUpdate)
            {
                throw new ConflictException(
                    "This role was modified by another user after it was loaded. Reload the role and try again.");
            }

            // Only what the real DeactivateAsync writes.
            var stored = ById(role.RoleId);
            stored.IsActive = role.IsActive;
            stored.UpdatedAt = role.UpdatedAt;
            stored.UpdatedBy = role.UpdatedBy;

            return Task.FromResult(role);
        }

        public Task<Role> UpdateAsync(Role role, CancellationToken cancellationToken)
        {
            if (ConflictOnUpdate)
            {
                throw new ConflictException(
                    "This role was modified by another user after it was loaded. Reload the role and try again.");
            }

            // Only what the real UpdateAsync writes.
            var stored = ById(role.RoleId);
            stored.RoleCode = role.RoleCode;
            stored.RoleName = role.RoleName;
            stored.Description = role.Description;
            stored.UpdatedAt = role.UpdatedAt;
            stored.UpdatedBy = role.UpdatedBy;

            return Task.FromResult(role);
        }
    }

    private sealed class InMemoryPermissionRepository : IPermissionRepository
    {
        private readonly List<Module> _modules;
        private readonly List<Permission> _permissions = new();

        public InMemoryPermissionRepository(params Module[] modules)
        {
            _modules = modules.ToList();
        }

        public int RowCountForRole(int roleId) => _permissions.Count(p => p.RoleId == roleId);

        public IReadOnlyList<Permission> RowsForRole(int roleId) => _permissions.Where(p => p.RoleId == roleId).ToList();

        public Task<PermissionActionFlags?> GetPermissionFlagsAsync(
            string roleCode, string moduleCode, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Authorization is faked in these tests.");

        public Task<IReadOnlyList<Module>> GetActiveModulesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Module>>(_modules.Where(m => m.IsActive).OrderBy(m => m.SortOrder).ToList());

        public Task<IReadOnlyList<Permission>> GetByRoleIdAsync(int roleId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Permission>>(_permissions.Where(p => p.RoleId == roleId).ToList());

        public Task SaveRolePermissionsAsync(
            int roleId, IReadOnlyList<Permission> permissions, CancellationToken cancellationToken)
        {
            foreach (var incoming in permissions)
            {
                var existing = _permissions.FirstOrDefault(p => p.RoleId == roleId && p.ModuleId == incoming.ModuleId);
                if (existing is not null)
                {
                    _permissions.Remove(existing);
                }

                incoming.RoleId = roleId;
                _permissions.Add(incoming);
            }

            return Task.CompletedTask;
        }
    }

    private sealed class StubUserRepository : IUserRepository
    {
        /// <summary>Role ids that currently have at least one active user assigned.</summary>
        public HashSet<int> RoleIdsWithActiveUsers { get; } = new();

        public Task<bool> AnyActiveByRoleIdAsync(int roleId, CancellationToken cancellationToken) =>
            Task.FromResult(RoleIdsWithActiveUsers.Contains(roleId));

        public Task<bool> ExistsByLoginIdAsync(string loginId, int? excludeUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed here.");

        public Task<User> AddAsync(User user, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed here.");

        public Task<User> UpdateAsync(User user, byte[] originalRowVersion, bool passwordChanged, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed here.");

        public Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken) =>
            Task.FromResult<User?>(userId switch
            {
                1 => new User { UserId = 1, UserName = "Test Admin", RoleId = 1 },
                2 => new User { UserId = 2, UserName = "Test Manager", RoleId = 2 },
                _ => null,
            });

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<User?> GetByLoginIdForAuthenticationAsync(string loginId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task UpdateLastLoginAtAsync(int userId, DateTime lastLoginAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class RecordingAuditLogService : IAuditLogService
    {
        public List<AuditLogEntry> Entries { get; } = new();

        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingPermissionAuthorizationService : IPermissionAuthorizationService
    {
        private readonly bool _grant;

        public RecordingPermissionAuthorizationService(bool grant)
        {
            _grant = grant;
        }

        public List<(string RoleCode, string ModuleCode, PermissionAction Action)> Checks { get; } = new();

        public Task<bool> HasPermissionAsync(
            string roleCode, string moduleCode, PermissionAction action, CancellationToken cancellationToken)
        {
            Checks.Add((roleCode, moduleCode, action));
            return Task.FromResult(_grant);
        }
    }
}
