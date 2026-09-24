using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Proves Step 8's [RequirePermission] mechanism end-to-end through GET /api/v1/users
/// (ModuleCodes.AdminUsers, PermissionAction.View) - the one controlled endpoint this step
/// secures. Uses the real IJwtTokenService (no DB dependency) to mint genuinely valid, signed
/// tokens, and a configurable fake IPermissionAuthorizationService in place of the real one
/// (which needs a live SQL Server this environment does not have).
/// </summary>
public class AuthorizationTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public AuthorizationTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private (HttpClient Client, WebApplicationFactory<Program> Factory) CreateClient(FakePermissionAuthorizationService permissionService)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserService>();
                services.AddSingleton<IUserService>(FakeUserService.Empty());
                services.RemoveAll<IPermissionAuthorizationService>();
                services.AddSingleton<IPermissionAuthorizationService>(permissionService);
            });
        });

        return (factory.CreateClient(), factory);
    }

    private static string MintToken(WebApplicationFactory<Program> factory, string roleCode)
    {
        var jwtTokenService = factory.Services.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtTokenService.GenerateAccessToken(new User
        {
            UserId = 1,
            LoginId = "test-user",
            Role = new Role { RoleCode = roleCode, RoleName = roleCode },
        });
        return token;
    }

    [Fact]
    public async Task GetUsers_Returns401_WhenNoTokenIsSupplied()
    {
        var (client, _) = CreateClient(FakePermissionAuthorizationService.AlwaysAllow());

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task GetUsers_Returns200_WhenAuthenticatedAndPermissionGranted()
    {
        var (client, factory) = CreateClient(FakePermissionAuthorizationService.AlwaysAllow());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(factory, "ADMIN"));

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_Returns403_WhenAuthenticatedButPermissionDenied()
    {
        var (client, factory) = CreateClient(FakePermissionAuthorizationService.AlwaysDeny());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(factory, "PRODUCTION_USER"));

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task GetUsers_Returns403_NotUnauthorized_ForAuthenticatedUserLackingPermission()
    {
        // Explicit per spec: an authenticated-but-not-permitted request must be 403, never 401.
        var (client, factory) = CreateClient(FakePermissionAuthorizationService.AlwaysDeny());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(factory, "MANAGEMENT"));

        var response = await client.GetAsync("/api/v1/users");

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SecurityTest_RoleWithoutPermission_Gets403_SameRoleModuleWithPermission_Gets200()
    {
        // Mirrors the spec's "Role A: Machine.can_delete=false / Role B: Machine.can_delete=true"
        // scenario, adapted to the actual secured endpoint (Users/View, since no Machine
        // controller/Delete action exists yet) - the point is identical: prove the database
        // permission is checked, not just the presence of a role claim.
        var permissionService = new FakePermissionAuthorizationService(allowedRoleCodes: new HashSet<string> { "MAINT_MANAGER" });
        var (client, factory) = CreateClient(permissionService);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(factory, "PRODUCTION_USER"));
        var deniedResponse = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(factory, "MAINT_MANAGER"));
        var allowedResponse = await client.GetAsync("/api/v1/users");
        Assert.Equal(HttpStatusCode.OK, allowedResponse.StatusCode);
    }

    [Fact]
    public async Task PermissionLookup_UsesTheAuthenticatedUsersOwnRole_NotAnyOtherRole()
    {
        // Only ADMIN is allowed by this fake - a token minted for a different role must not
        // somehow inherit ADMIN's access.
        var permissionService = new FakePermissionAuthorizationService(allowedRoleCodes: new HashSet<string> { "ADMIN" });
        var (client, factory) = CreateClient(permissionService);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(factory, "MAINT_ENGINEER"));
        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(1, permissionService.CallsForRoleCode("MAINT_ENGINEER"));
        Assert.Equal(0, permissionService.CallsForRoleCode("ADMIN"));
    }

    [Fact]
    public async Task RoleClaimAlone_DoesNotGrantAccess_WithoutADatabasePermission()
    {
        // A syntactically valid role claim, but the fake (standing in for permission_master)
        // denies everything - proves the role claim by itself is not treated as sufficient.
        var (client, factory) = CreateClient(FakePermissionAuthorizationService.AlwaysDeny());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", MintToken(factory, "ADMIN"));

        var response = await client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Login_RemainsPubliclyAccessible_WithoutAnyToken()
    {
        var (client, _) = CreateClient(FakePermissionAuthorizationService.AlwaysDeny());

        // No Authorization header at all - login must not be gated by [RequirePermission].
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest { LoginId = "admin", Password = "wrong" });

        // 400 (invalid credentials, via AuthService/ValidationException) - specifically NOT 401.
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class FakeUserService : IUserService
    {
        public static FakeUserService Empty() => new();

        public Task<PagedResult<UserDto>> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(PagedResult<UserDto>.Create(Array.Empty<UserDto>(), request.PageNumber, request.PageSize, totalCount: 0));

        public Task<UserDto> GetByIdAsync(int userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by AuthorizationTests.");

        public Task<UserDto> CreateAsync(CreateUserRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by this test.");

        public Task<UserDto> UpdateAsync(int userId, UpdateUserRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by this test.");
    }

    private sealed class FakePermissionAuthorizationService : IPermissionAuthorizationService
    {
        private readonly bool? _fixedResult;
        private readonly HashSet<string>? _allowedRoleCodes;
        private readonly Dictionary<string, int> _callCountsByRoleCode = new();

        private FakePermissionAuthorizationService(bool fixedResult)
        {
            _fixedResult = fixedResult;
        }

        public FakePermissionAuthorizationService(HashSet<string> allowedRoleCodes)
        {
            _allowedRoleCodes = allowedRoleCodes;
        }

        public static FakePermissionAuthorizationService AlwaysAllow() => new(fixedResult: true);
        public static FakePermissionAuthorizationService AlwaysDeny() => new(fixedResult: false);

        public int CallsForRoleCode(string roleCode) => _callCountsByRoleCode.GetValueOrDefault(roleCode);

        public Task<bool> HasPermissionAsync(
            string roleCode, string moduleCode, PermissionAction action, CancellationToken cancellationToken)
        {
            _callCountsByRoleCode[roleCode] = _callCountsByRoleCode.GetValueOrDefault(roleCode) + 1;

            var allowed = _fixedResult ?? _allowedRoleCodes!.Contains(roleCode);
            return Task.FromResult(allowed);
        }
    }
}
