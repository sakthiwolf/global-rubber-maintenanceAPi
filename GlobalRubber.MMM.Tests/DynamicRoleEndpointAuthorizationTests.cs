using System.Net;
using System.Text;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
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
/// A dynamically created role ("TESTER") whose permission_master row is changed one flag at a time, through the
/// real HTTP pipeline with the REAL PermissionAuthorizationService (JWT role claim -> permission_master lookup).
/// Only the row lookup is faked (an in-memory matrix, see <see cref="MatrixPermissionRepository"/>): the real one
/// needs a live SQL Server. Machines/Molds have no API yet, so the module exercised here is ADMIN_USERS
/// (GET = View, POST = Add, PUT = Edit) and ADMIN_ROLES (Delete) - the same rules that will apply to them.
/// Each action is decided independently: View never implies Add/Edit/Delete, and Add/Edit never imply View.
/// </summary>
public class DynamicRoleEndpointAuthorizationTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string Tester = "TESTER";
    private const int TesterRoleId = 5;
    private const int TesterUserId = 5;

    private readonly ApiWebApplicationFactory _factory;

    public DynamicRoleEndpointAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(
        HttpClient Client, WebApplicationFactory<Program> Factory, InMemoryUserRepository Users, MatrixPermissionRepository Matrix);

    /// <summary>Signed in as TESTER (user 5, role TESTER) unless told otherwise.</summary>
    private H CreateHarness(bool authenticate = true, string roleCode = Tester, int userId = TesterUserId)
    {
        var roleList = UserTestData.Roles();
        roleList.Add(new Role { RoleId = TesterRoleId, RoleCode = Tester, RoleName = "Tester", IsActive = true });
        var roles = new SimpleRoleRepository(roleList);

        var userList = UserTestData.Users();
        userList.Add(new User
        {
            UserId = TesterUserId, UserCode = "USR-0005", LoginId = "tester", UserName = "Tester", RoleId = TesterRoleId,
            PasswordHash = Sha256Hasher.Hash("x"), IsActive = true,
            CreatedAt = new DateTime(2026, 1, 3, 0, 0, 0, DateTimeKind.Utc), RowVersion = new byte[] { 1 },
        });
        var users = new InMemoryUserRepository(userList, roles.Find);
        var matrix = new MatrixPermissionRepository();

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(users);
                services.RemoveAll<IRoleRepository>();
                services.AddSingleton<IRoleRepository>(roles);
                services.RemoveAll<IPermissionRepository>();
                services.AddSingleton<IPermissionRepository>(matrix);
                services.RemoveAll<IAuditLogService>();
                services.AddSingleton<IAuditLogService>(new RecordingAuditLog());
                // IPermissionAuthorizationService is deliberately NOT replaced: the real one is under test.
            });
        });

        var client = factory.CreateClient();
        if (authenticate)
        {
            TestAuth.Authenticate(client, factory, roleCode, userId);
        }

        return new H(client, factory, users, matrix);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    private static string CreateBody() =>
        JsonSerializer.Serialize(new { loginId = "newperson", userName = "New Person", roleId = 3, password = "Correct horse 9!", isActive = true });

    private static string UpdateBody(H h) =>
        JsonSerializer.Serialize(new
        {
            loginId = "ravi", userName = "Ravi K", email = "ravi@example.com", mobile = "9840011122", roleId = 2, isActive = true,
            newPassword = (string?)null, rowVersion = Convert.ToBase64String(h.Users.Stored(2).RowVersion),
        });

    private static Task<HttpResponseMessage> List(H h) => h.Client.GetAsync("/api/v1/users");
    private static Task<HttpResponseMessage> GetOne(H h) => h.Client.GetAsync("/api/v1/users/2");
    private static Task<HttpResponseMessage> Create(H h) => h.Client.PostAsync("/api/v1/users", Json(CreateBody()));
    private static Task<HttpResponseMessage> Update(H h) => h.Client.PutAsync("/api/v1/users/2", Json(UpdateBody(h)));

    // ================================================================ the exact TESTER scenario, step by step

    [Fact]
    public async Task Tester_ViewOnly_CanRead_ButAddAndEditAreForbidden_AndNothingChanges()
    {
        var h = CreateHarness();
        h.Matrix.Set(Tester, ModuleCodes.AdminUsers, view: true);

        Assert.Equal(HttpStatusCode.OK, (await List(h)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await GetOne(h)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Create(h)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Update(h)).StatusCode);

        Assert.Equal(3, h.Users.Count);
        Assert.Equal("Ravi Kumar", h.Users.Stored(2).UserName);
    }

    [Fact]
    public async Task Tester_PermissionChangesTakeEffectImmediately_OneFlagAtATime()
    {
        var h = CreateHarness();

        // View only.
        h.Matrix.Set(Tester, ModuleCodes.AdminUsers, view: true);
        Assert.Equal(HttpStatusCode.OK, (await List(h)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Update(h)).StatusCode);

        // + Edit: Edit works, Add still does not.
        h.Matrix.Set(Tester, ModuleCodes.AdminUsers, view: true, edit: true);
        Assert.Equal(HttpStatusCode.OK, (await Update(h)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Create(h)).StatusCode);

        // + Add.
        h.Matrix.Set(Tester, ModuleCodes.AdminUsers, view: true, add: true, edit: true);
        Assert.Equal(HttpStatusCode.Created, (await Create(h)).StatusCode);

        // View switched off: reads are refused even though Add/Edit remain granted.
        h.Matrix.Set(Tester, ModuleCodes.AdminUsers, view: false, add: true, edit: true);
        Assert.Equal(HttpStatusCode.Forbidden, (await List(h)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetOne(h)).StatusCode);
    }

    // ================================================================ every combination

    public static IEnumerable<object[]> ViewAddEditCombinations() =>
        from view in new[] { false, true }
        from add in new[] { false, true }
        from edit in new[] { false, true }
        select new object[] { view, add, edit };

    [Theory]
    [MemberData(nameof(ViewAddEditCombinations))]
    public async Task EachEndpoint_IsForbidden_ExactlyWhenItsOwnActionIsMissing(bool view, bool add, bool edit)
    {
        var h = CreateHarness();
        h.Matrix.Set(Tester, ModuleCodes.AdminUsers, view: view, add: add, edit: edit);

        Assert.Equal(!view, (await List(h)).StatusCode == HttpStatusCode.Forbidden);
        Assert.Equal(!view, (await GetOne(h)).StatusCode == HttpStatusCode.Forbidden);
        Assert.Equal(!add, (await Create(h)).StatusCode == HttpStatusCode.Forbidden);
        Assert.Equal(!edit, (await Update(h)).StatusCode == HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NoPermissionRow_MeansEveryEndpointIsForbidden()
    {
        var h = CreateHarness(); // TESTER has no row at all

        Assert.Equal(HttpStatusCode.Forbidden, (await List(h)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await GetOne(h)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Create(h)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Update(h)).StatusCode);
    }

    [Fact]
    public async Task ApproveAndExport_DoNotGrantAnythingElse()
    {
        var h = CreateHarness();
        h.Matrix.Set(Tester, ModuleCodes.AdminUsers, approve: true, export: true);

        Assert.Equal(HttpStatusCode.Forbidden, (await List(h)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Create(h)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Update(h)).StatusCode);
    }

    [Fact]
    public async Task Permissions_AreScopedToTheModule_AndToTheRole()
    {
        var h = CreateHarness();
        // Full rights on another module, and full rights for ANOTHER role on this one: neither helps TESTER.
        h.Matrix.Set(Tester, ModuleCodes.MasterMachine, view: true, add: true, edit: true, delete: true, approve: true, export: true);
        h.Matrix.Set("ADMIN", ModuleCodes.AdminUsers, view: true, add: true, edit: true, delete: true);

        Assert.Equal(HttpStatusCode.Forbidden, (await List(h)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Create(h)).StatusCode);
    }

    [Fact]
    public async Task DeleteOnly_OnRoles_DoesNotGrantViewAddOrEdit()
    {
        var h = CreateHarness();
        h.Matrix.Set(Tester, ModuleCodes.AdminRoles, delete: true);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/roles")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/roles/3/permissions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PostAsync("/api/v1/roles", Json("{\"roleCode\":\"X\",\"roleName\":\"X\"}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync("/api/v1/roles/3", Json("{\"roleCode\":\"X\",\"roleName\":\"X\"}"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync("/api/v1/roles/3/permissions", Json("{\"permissions\":[]}"))).StatusCode);
    }

    [Fact]
    public async Task WithoutDelete_DeleteRoleIsForbidden_EvenWithEveryOtherPermission()
    {
        var h = CreateHarness();
        h.Matrix.Set(Tester, ModuleCodes.AdminRoles, view: true, add: true, edit: true, approve: true, export: true);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync("/api/v1/roles/3")).StatusCode);
    }

    // ================================================================ 401 vs 403, and the matrix endpoint

    [Fact]
    public async Task WithoutAToken_EveryProtectedEndpointReturns401_NotFourOhThree()
    {
        var h = CreateHarness(authenticate: false);
        h.Matrix.Set(Tester, ModuleCodes.AdminUsers, view: true, add: true, edit: true, delete: true);

        Assert.Equal(HttpStatusCode.Unauthorized, (await List(h)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Create(h)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Update(h)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.DeleteAsync("/api/v1/roles/3")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/auth/me/permissions")).StatusCode);
    }

    [Fact]
    public async Task MyPermissions_ReflectsTheBackendMatrix_ForTheSignedInUsersRole()
    {
        var h = CreateHarness();
        h.Matrix.Set(Tester, ModuleCodes.AdminUsers, view: true, edit: true);
        h.Matrix.Set("ADMIN", ModuleCodes.AdminUsers, view: true, add: true, edit: true, delete: true);

        var response = await h.Client.GetAsync("/api/v1/auth/me/permissions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("data");
        Assert.Equal(Tester, data.GetProperty("roleCode").GetString());
        var users = data.GetProperty("permissions").EnumerateArray().Single(p => p.GetProperty("moduleCode").GetString() == ModuleCodes.AdminUsers);
        Assert.True(users.GetProperty("canView").GetBoolean());
        Assert.False(users.GetProperty("canAdd").GetBoolean());
        Assert.True(users.GetProperty("canEdit").GetBoolean());
        Assert.False(users.GetProperty("canDelete").GetBoolean());
        Assert.False(users.GetProperty("canApprove").GetBoolean());
        Assert.False(users.GetProperty("canExport").GetBoolean());
    }

    // ---- in-memory permission_master: (role code, module code) -> six flags; a missing row means no access ----
    internal sealed class MatrixPermissionRepository : IPermissionRepository
    {
        private static readonly string[] AllModules =
        {
            ModuleCodes.Dashboard, ModuleCodes.MasterMachine, ModuleCodes.AdminUsers, ModuleCodes.AdminRoles,
        };

        private readonly Dictionary<(string Role, string Module), PermissionActionFlags> _rows = new();
        private readonly object _gate = new();

        public void Set(string role, string module, bool view = false, bool add = false, bool edit = false,
            bool delete = false, bool approve = false, bool export = false)
        {
            lock (_gate)
            {
                _rows[(role, module)] = new PermissionActionFlags(view, add, edit, delete, approve, export);
            }
        }

        public Task<PermissionActionFlags?> GetPermissionFlagsAsync(string roleCode, string moduleCode, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                return Task.FromResult(_rows.TryGetValue((roleCode, moduleCode), out var flags) ? flags : null);
            }
        }

        public Task<IReadOnlyList<Module>> GetActiveModulesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Module>>(AllModules
                .Select((code, i) => new Module { ModuleId = i + 1, ModuleCode = code, ModuleName = code, MenuGroup = "Test", SortOrder = i + 1 })
                .ToList());

        public Task<IReadOnlyList<Permission>> GetByRoleIdAsync(int roleId, CancellationToken cancellationToken)
        {
            // Only TESTER's role id is used with this method in these tests.
            var rows = new List<Permission>();
            lock (_gate)
            {
                foreach (var ((_, module), f) in _rows.Where(r => r.Key.Role == Tester))
                {
                    rows.Add(new Permission
                    {
                        RoleId = roleId, ModuleId = Array.IndexOf(AllModules, module) + 1, CanView = f.CanView, CanAdd = f.CanAdd,
                        CanEdit = f.CanEdit, CanDelete = f.CanDelete, CanApprove = f.CanApprove, CanExport = f.CanExport,
                    });
                }
            }

            return Task.FromResult<IReadOnlyList<Permission>>(rows);
        }

        public Task SaveRolePermissionsAsync(int roleId, IReadOnlyList<Permission> permissions, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
