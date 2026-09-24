using System.Net;
using System.Text;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The authorization contract of every role/permission endpoint, checked through the real pipeline
/// (real JWT validation + [RequirePermission] + GlobalExceptionHandler) with the role/permission
/// SERVICES replaced by counters - so a 401/403 is provably decided before any business code runs.
///
/// Required permission per endpoint: GET* -> AdminRoles.View, POST -> Add, PUT role -> Edit,
/// DELETE -> Delete, PUT permissions -> Edit. Only the (module, action) pair is asserted - never a
/// role name: which role may do what lives in permission_master, faked here by StubPermissionAuthorization.
/// </summary>
public class RoleAuthorizationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public RoleAuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    public static IEnumerable<object[]> ProtectedEndpoints() => new[]
    {
        new object[] { "GET", "/api/v1/roles", PermissionAction.View },
        new object[] { "GET", "/api/v1/roles/2", PermissionAction.View },
        new object[] { "POST", "/api/v1/roles", PermissionAction.Add },
        new object[] { "PUT", "/api/v1/roles/2", PermissionAction.Edit },
        new object[] { "DELETE", "/api/v1/roles/2", PermissionAction.Delete },
        new object[] { "GET", "/api/v1/roles/2/permissions", PermissionAction.View },
        new object[] { "PUT", "/api/v1/roles/2/permissions", PermissionAction.Edit },
    };

    private sealed record Setup(
        HttpClient Client,
        WebApplicationFactory<Program> Factory,
        StubPermissionAuthorization Authorization,
        CountingRoleService Roles,
        CountingPermissionService Permissions);

    private Setup CreateSetup(StubPermissionAuthorization authorization)
    {
        var roles = new CountingRoleService();
        var permissions = new CountingPermissionService();

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRoleService>();
                services.AddSingleton<IRoleService>(roles);
                services.RemoveAll<IPermissionService>();
                services.AddSingleton<IPermissionService>(permissions);
                services.RemoveAll<IPermissionAuthorizationService>();
                services.AddSingleton<IPermissionAuthorizationService>(authorization);
            });
        });

        return new Setup(factory.CreateClient(), factory, authorization, roles, permissions);
    }

    private static HttpRequestMessage BuildRequest(string method, string url)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), url);

        if (method == "POST" || method == "PUT")
        {
            var json = url.EndsWith("/permissions", StringComparison.Ordinal)
                ? """{"permissions":[]}"""
                : """{"roleCode":"SOME_ROLE","roleName":"Some Role"}""";
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        }

        return request;
    }

    // ---- 1: no token -> 401 ----

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task NoToken_Returns401_AndNoBusinessCodeRuns(string method, string url, PermissionAction _)
    {
        var s = CreateSetup(new StubPermissionAuthorization(grantAll: true));

        var response = await s.Client.SendAsync(BuildRequest(method, url));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(0, s.Roles.Calls + s.Permissions.Calls);
        Assert.Empty(s.Authorization.Checks); // never even reached the permission lookup
    }

    // ---- 2: valid token, permission denied -> 403 (not 401) ----

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task ValidToken_WithoutThePermission_Returns403_NotUnauthorized_AndNoBusinessCodeRuns(string method, string url, PermissionAction _)
    {
        var s = CreateSetup(new StubPermissionAuthorization(grantAll: false));
        TestAuth.Authenticate(s.Client, s.Factory, "MAINT_ENGINEER");

        var response = await s.Client.SendAsync(BuildRequest(method, url));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(text);
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("You do not have permission to perform this action.", body.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("ADMIN_ROLES", text); // no module codes / permission details leaked
        Assert.Equal(0, s.Roles.Calls + s.Permissions.Calls);
    }

    // ---- 3: valid token + granted -> success, and the endpoint asked for exactly the documented permission ----

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task ValidToken_WithThePermission_Succeeds_AfterCheckingExactlyTheDocumentedPermission(string method, string url, PermissionAction action)
    {
        var s = CreateSetup(new StubPermissionAuthorization(grantAll: true));
        TestAuth.Authenticate(s.Client, s.Factory, "SOME_CUSTOM_ROLE");

        var response = await s.Client.SendAsync(BuildRequest(method, url));

        Assert.True(response.IsSuccessStatusCode, $"{method} {url} -> {(int)response.StatusCode}");
        var check = Assert.Single(s.Authorization.Checks);
        Assert.Equal(("SOME_CUSTOM_ROLE", ModuleCodes.AdminRoles, action), check);
        Assert.Equal(1, s.Roles.Calls + s.Permissions.Calls);
    }

    // ---- 4: the role claim alone grants nothing ----

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task RoleClaimAlone_GrantsNothing_EvenForTheAdminRoleCode(string method, string url, PermissionAction _)
    {
        // permission_master (faked) grants nothing; the token still says role ADMIN.
        var s = CreateSetup(new StubPermissionAuthorization(grantAll: false));
        TestAuth.Authenticate(s.Client, s.Factory, "ADMIN");

        var response = await s.Client.SendAsync(BuildRequest(method, url));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, s.Roles.Calls + s.Permissions.Calls);
    }

    // ---- 5: the authenticated user's own role is what gets checked ----

    [Fact]
    public async Task PermissionLookup_UsesTheAuthenticatedUsersRole_AndNoOther()
    {
        // Only role code ADMIN is granted, and only by the lookup - nothing in the code names it.
        var authorization = new StubPermissionAuthorization((role, module, action) => role == "ADMIN");
        var s = CreateSetup(authorization);

        TestAuth.Authenticate(s.Client, s.Factory, "MAINT_ENGINEER");
        var denied = await s.Client.SendAsync(BuildRequest("GET", "/api/v1/roles"));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        TestAuth.Authenticate(s.Client, s.Factory, "ADMIN");
        var allowed = await s.Client.SendAsync(BuildRequest("GET", "/api/v1/roles"));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        Assert.Equal(new[] { "MAINT_ENGINEER", "ADMIN" }, authorization.Checks.Select(c => c.RoleCode).ToArray());
    }

    // ---- the caller's own permissions: any authenticated user, no AdminRoles permission needed ----

    [Fact]
    public async Task MyPermissions_Returns401_WithoutToken()
    {
        var s = CreateSetup(new StubPermissionAuthorization(grantAll: true));

        var response = await s.Client.GetAsync("/api/v1/auth/me/permissions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(s.Permissions.MyPermissionsUserIds);
    }

    [Fact]
    public async Task MyPermissions_IsAvailableToAnyAuthenticatedUser_WithoutAnyAdminRolesPermission()
    {
        // Every permission denied - a Production User has no AdminRoles.View - yet loading one's OWN
        // permissions must still work, or the app could not build that user's menu at all.
        var s = CreateSetup(new StubPermissionAuthorization(grantAll: false));
        TestAuth.Authenticate(s.Client, s.Factory, "PRODUCTION_USER", userId: 42);

        var response = await s.Client.GetAsync("/api/v1/auth/me/permissions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { 42 }, s.Permissions.MyPermissionsUserIds); // the token's own user id, nothing from the request
        Assert.Empty(s.Authorization.Checks);
    }

    // ---- 401 vs 403 stay distinct on a bad token ----

    [Fact]
    public async Task GarbageToken_Returns401()
    {
        var s = CreateSetup(new StubPermissionAuthorization(grantAll: true));
        s.Client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-jwt");

        var response = await s.Client.SendAsync(BuildRequest("GET", "/api/v1/roles"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- counters ----

    private sealed class CountingRoleService : IRoleService
    {
        private int _calls;
        public int Calls => _calls;

        private static readonly RoleDto Sample = new() { RoleId = 2, RoleCode = "SOME_ROLE", RoleName = "Some Role", IsActive = true };

        public Task<PagedResult<RoleDto>> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(PagedResult<RoleDto>.Create(new[] { Sample }, request.PageNumber, request.PageSize, 1));
        }

        public Task<RoleDto> GetByIdAsync(int roleId, CancellationToken cancellationToken) => Count();

        public Task<RoleDto> CreateAsync(CreateRoleRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken) => Count();

        public Task<RoleDto> UpdateAsync(int roleId, UpdateRoleRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken) => Count();

        public Task<RoleDto> DeactivateAsync(int roleId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken) => Count();

        private Task<RoleDto> Count()
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(Sample);
        }
    }

    private sealed class CountingPermissionService : IPermissionService
    {
        private int _calls;
        public int Calls => _calls;
        public List<int> MyPermissionsUserIds { get; } = new();

        private static RolePermissionMatrixDto Matrix() => new() { RoleId = 2, RoleCode = "SOME_ROLE", RoleName = "Some Role" };

        public Task<RolePermissionMatrixDto> GetRolePermissionsAsync(int roleId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(Matrix());
        }

        public Task<RolePermissionMatrixDto> UpdateRolePermissionsAsync(
            int roleId, UpdateRolePermissionsRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(Matrix());
        }

        public Task<RolePermissionMatrixDto> GetMyPermissionsAsync(int userId, CancellationToken cancellationToken)
        {
            lock (MyPermissionsUserIds)
            {
                MyPermissionsUserIds.Add(userId);
            }

            return Task.FromResult(Matrix());
        }
    }
}
