using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Exercises the real HTTP pipeline (routing, model binding, ApiResponse envelope,
/// GlobalExceptionHandler) with <see cref="IUserService"/> swapped for an in-memory fake, since
/// the real <c>UserService</c>/<c>UserRepository</c> require a live SQL Server connection that
/// this environment does not have. GetAll is also protected by [RequirePermission] as of Step 8
/// (see AuthorizationTests.cs for the dedicated 401/403/permission-matrix coverage) - here it
/// only needs a token that a permissive fake IPermissionAuthorizationService will accept, since
/// this file's own purpose is exercising IUserService, not authorization itself.
/// </summary>
public class UserControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public UserControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    /// <summary>GetById is protected by [RequirePermission] as well, so it uses the authenticated client too.</summary>
    private HttpClient CreateClientWithFakeUserService(FakeUserService fake) =>
        CreateAuthenticatedClientWithFakeUserService(fake);

    /// <summary>For GetAll, which [RequirePermission] does protect - needs a real, validly
    /// signed token plus a permissive fake IPermissionAuthorizationService (this file exercises
    /// IUserService, not authorization itself - see AuthorizationTests.cs for that).</summary>
    private HttpClient CreateAuthenticatedClientWithFakeUserService(FakeUserService fake)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IUserService>();
                services.AddSingleton<IUserService>(fake);
                services.RemoveAll<IPermissionAuthorizationService>();
                services.AddSingleton<IPermissionAuthorizationService>(new AlwaysAllowPermissionAuthorizationService());
            });
        });

        var client = factory.CreateClient();
        var jwtTokenService = factory.Services.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtTokenService.GenerateAccessToken(new User
        {
            UserId = 1,
            LoginId = "test-user",
            Role = new Role { RoleCode = "ADMIN", RoleName = "Administrator" },
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return client;
    }

    [Fact]
    public async Task GetAll_Returns200_WithPagedResultEnvelope_AndNoPasswordData()
    {
        var client = CreateAuthenticatedClientWithFakeUserService(FakeUserService.WithSampleUsers());

        var response = await client.GetAsync("/api/v1/users?pageNumber=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        var data = root.GetProperty("data");
        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, data.GetProperty("pageNumber").GetInt32());
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());

        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetById_Returns200_WithUser_WhenFound()
    {
        var client = CreateClientWithFakeUserService(FakeUserService.WithSampleUsers());

        var response = await client.GetAsync("/api/v1/users/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");

        Assert.Equal("USR-0001", data.GetProperty("userCode").GetString());
        Assert.Equal("admin", data.GetProperty("loginId").GetString());
        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetById_Returns404_ThroughGlobalExceptionHandler_WhenUserDoesNotExist()
    {
        var client = CreateClientWithFakeUserService(FakeUserService.WithSampleUsers());

        var response = await client.GetAsync("/api/v1/users/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
    }

    private sealed class FakeUserService : IUserService
    {
        private readonly List<UserDto> _users;

        private FakeUserService(List<UserDto> users)
        {
            _users = users;
        }

        public static FakeUserService WithSampleUsers() => new(new List<UserDto>
        {
            new()
            {
                UserId = 1,
                UserCode = "USR-0001",
                LoginId = "admin",
                UserName = "Administrator",
                RoleId = 1,
                RoleName = "Administrator",
                IsActive = true,
                MustChangePassword = false,
            },
            new()
            {
                UserId = 2,
                UserCode = "USR-0002",
                LoginId = "manager",
                UserName = "Maintenance Manager",
                RoleId = 2,
                RoleName = "Maintenance Manager",
                IsActive = true,
                MustChangePassword = true,
            },
        });

        public Task<PagedResult<UserDto>> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(PagedResult<UserDto>.Create(_users, request.PageNumber, request.PageSize, _users.Count));

        public Task<UserDto> CreateAsync(CreateUserRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by this test.");

        public Task<UserDto> UpdateAsync(int userId, UpdateUserRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by this test.");

        public Task<UserDto> GetByIdAsync(int userId, CancellationToken cancellationToken)
        {
            var user = _users.FirstOrDefault(u => u.UserId == userId);

            return user is null
                ? throw new NotFoundException(nameof(User), userId)
                : Task.FromResult(user);
        }
    }

    private sealed class AlwaysAllowPermissionAuthorizationService : IPermissionAuthorizationService
    {
        public Task<bool> HasPermissionAsync(
            string roleCode, string moduleCode, PermissionAction action, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
