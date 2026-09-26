using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
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
/// /api/v1/users through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model
/// binding) with the REAL UserService, AuthService, password hasher and JWT service. Only persistence is
/// faked (in-memory, see UserTestFakes.cs) because the real repositories need a live SQL Server.
/// Acting user: id 1 "Sakthi". Roles: 1 ADMIN, 2 MAINT_MANAGER, 3 TEST, 4 OLD_ROLE (inactive).
/// </summary>
public class UserEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public UserEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(
        HttpClient Client,
        WebApplicationFactory<Program> Factory,
        InMemoryUserRepository Users,
        RecordingAuditLog Audit,
        StubPermissionAuthorization Authorization);

    private H CreateHarness(bool permissionGranted = true, bool authenticate = true)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var audit = new RecordingAuditLog();
        var authorization = new StubPermissionAuthorization(permissionGranted);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(users);
                services.RemoveAll<IRefreshTokenRepository>();
                services.AddSingleton<IRefreshTokenRepository>(new InMemoryRefreshTokenRepository()); // login also issues a refresh token
                services.RemoveAll<IRoleRepository>();
                services.AddSingleton<IRoleRepository>(roles);
                services.RemoveAll<IPermissionRepository>();
                services.AddSingleton<IPermissionRepository>(new MiniPermissionRepository());
                services.RemoveAll<IAuditLogService>();
                services.AddSingleton<IAuditLogService>(audit);
                services.RemoveAll<IPermissionAuthorizationService>();
                services.AddSingleton<IPermissionAuthorizationService>(authorization);
            });
        });

        var client = factory.CreateClient();
        if (authenticate)
        {
            TestAuth.Authenticate(client, factory, "ADMIN", 1);
        }

        return new H(client, factory, users, audit, authorization);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Root(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private const string Password = "Correct horse 9!";

    private static string CreateBody(string loginId = "tester2", int roleId = 3, bool isActive = true, string password = Password) =>
        JsonSerializer.Serialize(new { loginId, userName = "Tester Two", roleId, password, isActive });

    private static string UpdateBody(string rowVersion, string userName = "Ravi K", int roleId = 2, string? newPassword = null) =>
        JsonSerializer.Serialize(new { loginId = "ravi", userName, email = "ravi@example.com", mobile = "9840011122", roleId, isActive = true, newPassword, rowVersion });

    // ================================================================ POST /users

    [Fact]
    public async Task Post_Returns201_WithTheUser_AndALocationHeader_AndNeverThePasswordOrHash()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/users", Json(CreateBody()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var text = await response.Content.ReadAsStringAsync();
        var data = JsonDocument.Parse(text).RootElement.GetProperty("data");
        Assert.Equal("USR-0003", data.GetProperty("userCode").GetString());
        Assert.Equal("tester2", data.GetProperty("loginId").GetString());
        Assert.Equal(3, data.GetProperty("roleId").GetInt32());
        Assert.Equal("test", data.GetProperty("roleName").GetString());
        Assert.True(data.GetProperty("isActive").GetBoolean());
        Assert.True(data.GetProperty("mustChangePassword").GetBoolean());
        Assert.False(string.IsNullOrEmpty(data.GetProperty("rowVersion").GetString()));

        var storedHash = h.Users.StoredByLogin("tester2").PasswordHash;
        Assert.DoesNotContain(Password, text);
        Assert.DoesNotContain(storedHash, text);
        Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Post_StoresARealSaltedHash_NotThePassword()
    {
        var h = CreateHarness();

        await h.Client.PostAsync("/api/v1/users", Json(CreateBody()));

        var storedHash = h.Users.StoredByLogin("tester2").PasswordHash;
        Assert.NotEqual(Password, storedHash);
        Assert.DoesNotContain(Password, storedHash);
        Assert.StartsWith("AQAAAA", storedHash); // ASP.NET Core Identity PBKDF2 v3 format from the existing hasher
        var hasher = h.Factory.Services.GetRequiredService<IPasswordHasher>();
        Assert.True(hasher.VerifyPassword(storedHash, Password));
        Assert.False(hasher.VerifyPassword(storedHash, "wrong"));
    }

    [Fact]
    public async Task Post_IgnoresClientSuppliedServerControlledFields()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/users", Json(
            """{"loginId":"tester2","userName":"T","roleId":3,"password":"pw","userId":99,"userCode":"HACK-9","passwordHash":"evil","mustChangePassword":false,"createdBy":42,"rowVersion":"AAAA","failedLoginCount":7}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = h.Users.StoredByLogin("tester2");
        Assert.Equal("USR-0003", stored.UserCode);
        Assert.NotEqual(99, stored.UserId);
        Assert.NotEqual("evil", stored.PasswordHash);
        Assert.True(stored.MustChangePassword);
        Assert.Equal(1, stored.CreatedBy); // the token's user, not 42
        Assert.Equal(0, stored.FailedLoginCount);
    }

    [Fact]
    public async Task Post_WritesExactlyOneUserCreatedAudit_ForTheAuthenticatedUser_WithNoSecret()
    {
        var h = CreateHarness();

        await h.Client.PostAsync("/api/v1/users", Json(CreateBody()));

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal("UserCreated", entry.Action);
        Assert.Equal(1, entry.UserId);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Equal("USR-0003", entry.RecordRef);
        var everything = JsonSerializer.Serialize(entry);
        Assert.DoesNotContain(Password, everything);
        Assert.DoesNotContain(h.Users.StoredByLogin("tester2").PasswordHash, everything);
    }

    [Fact]
    public async Task Post_Returns409_ForADuplicateLoginId_AndNothingLeaks()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/users", Json(CreateBody(loginId: "RAVI")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("already exists", text);
        Assert.DoesNotContain("UQ_", text);
        Assert.DoesNotContain("SqlException", text);
        Assert.DoesNotContain(Password, text);
        Assert.Equal(2, h.Users.Count);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Post_Returns404_ForAnUnknownRole_And400_ForAnInactiveRoleOrInvalidFields()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PostAsync("/api/v1/users", Json(CreateBody(roleId: 999)))).StatusCode);

        var inactive = await h.Client.PostAsync("/api/v1/users", Json(CreateBody(roleId: 4)));
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Contains("not active", (await inactive.Content.ReadAsStringAsync()));

        var invalid = await h.Client.PostAsync("/api/v1/users", Json(CreateBody(loginId: " ", password: "")));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await Root(invalid)).GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("LoginId is required.", errors);
        Assert.Contains("Password is required.", errors);

        Assert.Equal(2, h.Users.Count);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ authorization (401 / 403 / permission asked for)

    [Fact]
    public async Task Users_Endpoints_Return401_WithoutAToken_AndChangeNothing()
    {
        var h = CreateHarness(authenticate: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PostAsync("/api/v1/users", Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PutAsync("/api/v1/users/2", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/users/2")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/users")).StatusCode);
        Assert.Equal(2, h.Users.Count);
        Assert.Empty(h.Authorization.Checks);
    }

    [Fact]
    public async Task Users_Endpoints_Return403_WithoutTheAdminUsersPermission_AndChangeNothing()
    {
        var h = CreateHarness(permissionGranted: false);

        var post = await h.Client.PostAsync("/api/v1/users", Json(CreateBody()));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync("/api/v1/users/2", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/users/2")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/users")).StatusCode);

        Assert.Equal("You do not have permission to perform this action.", (await Root(post)).GetProperty("message").GetString());
        Assert.Equal(2, h.Users.Count);
        Assert.Equal("Ravi Kumar", h.Users.Stored(2).UserName);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Users_Endpoints_AskForExactlyTheDocumentedPermission()
    {
        var h = CreateHarness();
        var rv = Convert.ToBase64String(h.Users.Stored(2).RowVersion);

        await h.Client.PostAsync("/api/v1/users", Json(CreateBody()));
        await h.Client.PutAsync("/api/v1/users/2", Json(UpdateBody(rv)));
        await h.Client.GetAsync("/api/v1/users/2");
        await h.Client.GetAsync("/api/v1/users");

        Assert.Equal(
            new[] { PermissionAction.Add, PermissionAction.Edit, PermissionAction.View, PermissionAction.View },
            h.Authorization.Checks.Select(c => c.Action).ToArray());
        Assert.All(h.Authorization.Checks, c => Assert.Equal((("ADMIN", ModuleCodes.AdminUsers)), (c.RoleCode, c.ModuleCode)));
    }

    // ================================================================ GET

    [Fact]
    public async Task Get_ListsTheStoredUsers_WithDynamicRoleNames_RowVersions_AndNoSecrets()
    {
        var h = CreateHarness();
        await h.Client.PostAsync("/api/v1/users", Json(CreateBody())); // a user in the dynamically created role "test"

        var response = await h.Client.GetAsync("/api/v1/users?pageNumber=1&pageSize=10");

        var text = await response.Content.ReadAsStringAsync();
        var items = JsonDocument.Parse(text).RootElement.GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(3, items.Count);
        Assert.Equal("test", items.Single(i => i.GetProperty("loginId").GetString() == "tester2").GetProperty("roleName").GetString());
        Assert.All(items, i => Assert.False(string.IsNullOrEmpty(i.GetProperty("rowVersion").GetString())));
        Assert.DoesNotContain("sha256", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("AQAAAA", text);
        Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
    }

    // ================================================================ PUT /users/{id}

    [Fact]
    public async Task Put_UpdatesTheUser_WithTheRowVersionFromGet_AndReturnsAFreshOne()
    {
        var h = CreateHarness();
        var current = (await Root(await h.Client.GetAsync("/api/v1/users/2"))).GetProperty("data");
        var rowVersion = current.GetProperty("rowVersion").GetString()!;

        var response = await h.Client.PutAsync("/api/v1/users/2", Json(UpdateBody(rowVersion, userName: "Ravi K", roleId: 3)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("Ravi K", data.GetProperty("userName").GetString());
        Assert.Equal(3, data.GetProperty("roleId").GetInt32());
        Assert.Equal("test", data.GetProperty("roleName").GetString());
        Assert.NotEqual(rowVersion, data.GetProperty("rowVersion").GetString());
        Assert.Equal(1, h.Users.Stored(2).UpdatedBy);
        Assert.Equal("USR-0002", h.Users.Stored(2).UserCode); // untouched
    }

    [Fact]
    public async Task Put_WithAStaleRowVersion_Returns409_WithASafeMessage_AndChangesNothing()
    {
        var h = CreateHarness();
        var stale = Convert.ToBase64String(h.Users.Stored(2).RowVersion);
        h.Users.SimulateConcurrentModification(2);

        var response = await h.Client.PutAsync("/api/v1/users/2", Json(UpdateBody(stale, userName: "Late Edit")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("The user was modified by another user. Refresh the user and try again.", text);
        Assert.DoesNotContain("DbUpdateConcurrencyException", text);
        Assert.DoesNotContain("row_version", text);
        Assert.Equal("Ravi Kumar", h.Users.Stored(2).UserName);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns400_WhenTheRowVersionIsMissing_And404_ForAnUnknownUser()
    {
        var h = CreateHarness();

        var missing = await h.Client.PutAsync("/api/v1/users/2", Json("""{"loginId":"ravi","userName":"Ravi","roleId":2,"isActive":true}"""));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("RowVersion is required.", await missing.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PutAsync("/api/v1/users/999", Json(UpdateBody("AQ==")))).StatusCode);
    }

    [Fact]
    public async Task Put_ChangesThePassword_OnlyWhenNewPasswordIsSupplied()
    {
        var h = CreateHarness();
        var before = h.Users.Stored(2).PasswordHash;

        await h.Client.PutAsync("/api/v1/users/2", Json(UpdateBody(Convert.ToBase64String(h.Users.Stored(2).RowVersion))));
        Assert.Equal(before, h.Users.Stored(2).PasswordHash);

        var response = await h.Client.PutAsync("/api/v1/users/2", Json(UpdateBody(Convert.ToBase64String(h.Users.Stored(2).RowVersion), newPassword: "A brand new pw")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var after = h.Users.Stored(2).PasswordHash;
        Assert.NotEqual(before, after);
        Assert.True(h.Factory.Services.GetRequiredService<IPasswordHasher>().VerifyPassword(after, "A brand new pw"));
        Assert.DoesNotContain("A brand new pw", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("A brand new pw", JsonSerializer.Serialize(h.Audit.Entries));
    }

    [Fact]
    public async Task Put_WritesUserUpdated_AndUserRoleChanged_WhenTheRoleChanges()
    {
        var h = CreateHarness();

        await h.Client.PutAsync("/api/v1/users/2", Json(UpdateBody(Convert.ToBase64String(h.Users.Stored(2).RowVersion), roleId: 3)));

        Assert.Equal(new[] { "UserUpdated", "UserRoleChanged" }, h.Audit.Entries.Select(e => e.Action).ToArray());
        Assert.All(h.Audit.Entries, e => Assert.Equal(1, e.UserId));
        Assert.Contains("Role: MAINT_MANAGER -> TEST", h.Audit.Entries[1].Description);
    }

    // ================================================================ the complete flow: create -> login -> JWT -> permissions

    [Fact]
    public async Task ACreatedUser_CanLogIn_GetsAJwtWithTheirRole_AndLoadsTheirPermissions()
    {
        var h = CreateHarness();

        // 1. An admin creates the user (role 3 = the dynamically created "TEST" role).
        Assert.Equal(HttpStatusCode.Created, (await h.Client.PostAsync("/api/v1/users", Json(CreateBody()))).StatusCode);

        // 2. The new user logs in - no token, and the login ID is matched case-insensitively.
        var anonymous = h.Factory.CreateClient();
        var login = await anonymous.PostAsync("/api/v1/auth/login", Json(JsonSerializer.Serialize(new { loginId = "TESTER2", password = Password })));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginData = (await Root(login)).GetProperty("data");
        var token = loginData.GetProperty("token").GetString()!;
        Assert.Equal("tester2", loginData.GetProperty("user").GetProperty("loginId").GetString());
        Assert.True(loginData.GetProperty("user").GetProperty("mustChangePassword").GetBoolean());
        Assert.DoesNotContain("passwordHash", await login.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);

        // 3. The JWT carries THEIR role.
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal("TEST", jwt.Claims.Single(c => c.Type is "role" or ClaimTypes.Role).Value);
        Assert.Equal(h.Users.StoredByLogin("tester2").UserId.ToString(), jwt.Claims.Single(c => c.Type is "sub" or ClaimTypes.NameIdentifier).Value);

        // 4. Their own permission matrix loads (any authenticated user) and reflects THEIR role, not another's.
        var me = h.Factory.CreateClient();
        me.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var permissions = await me.GetAsync("/api/v1/auth/me/permissions");
        Assert.Equal(HttpStatusCode.OK, permissions.StatusCode);
        var matrix = (await Root(permissions)).GetProperty("data");
        Assert.Equal(3, matrix.GetProperty("roleId").GetInt32());
        Assert.Equal("TEST", matrix.GetProperty("roleCode").GetString());
        var dashboard = matrix.GetProperty("permissions").EnumerateArray().Single(m => m.GetProperty("moduleCode").GetString() == ModuleCodes.Dashboard);
        Assert.True(dashboard.GetProperty("canView").GetBoolean());
    }

    [Fact]
    public async Task ACreatedUser_CannotLogIn_WithAWrongPassword_OrWhenCreatedInactive()
    {
        var h = CreateHarness();
        await h.Client.PostAsync("/api/v1/users", Json(CreateBody(loginId: "tester2")));
        await h.Client.PostAsync("/api/v1/users", Json(CreateBody(loginId: "sleeper", isActive: false)));
        var anonymous = h.Factory.CreateClient();

        var wrong = await anonymous.PostAsync("/api/v1/auth/login", Json(JsonSerializer.Serialize(new { loginId = "tester2", password = "nope" })));
        var inactive = await anonymous.PostAsync("/api/v1/auth/login", Json(JsonSerializer.Serialize(new { loginId = "sleeper", password = Password })));

        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Equal((await Root(wrong)).GetProperty("message").GetString(), (await Root(inactive)).GetProperty("message").GetString()); // one generic message
    }

    // ---- fake permission matrix source for GET /auth/me/permissions ----
    private sealed class MiniPermissionRepository : IPermissionRepository
    {
        public Task<IReadOnlyList<Module>> GetActiveModulesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Module>>(new List<Module>
            {
                new() { ModuleId = 1, ModuleCode = ModuleCodes.Dashboard, ModuleName = "Dashboard", MenuGroup = "Dashboard", SortOrder = 1 },
                new() { ModuleId = 2, ModuleCode = ModuleCodes.AdminUsers, ModuleName = "Users", MenuGroup = "Administration", SortOrder = 2 },
            });

        // Only role 3 (TEST) has a row: Dashboard.View.
        public Task<IReadOnlyList<Permission>> GetByRoleIdAsync(int roleId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Permission>>(roleId == 3 ? new List<Permission> { new() { RoleId = 3, ModuleId = 1, CanView = true } } : new List<Permission>());

        public Task<PermissionActionFlags?> GetPermissionFlagsAsync(string roleCode, string moduleCode, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Authorization is stubbed in these tests.");

        public Task SaveRolePermissionsAsync(int roleId, IReadOnlyList<Permission> permissions, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
