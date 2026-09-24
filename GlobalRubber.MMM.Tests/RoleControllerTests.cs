using System.Net;
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
/// Exercises the real HTTP pipeline with <see cref="IRoleService"/> swapped for an in-memory
/// fake, same approach as <see cref="UserControllerTests"/> and for the same reason: the real
/// <c>RoleService</c>/<c>RoleRepository</c> require a live SQL Server connection this
/// environment does not have.
/// </summary>
public class RoleControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public RoleControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClientWithFakeRoleService(FakeRoleService fake) =>
        _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IRoleService>();
                services.AddSingleton<IRoleService>(fake);
            });
        }).CreateClient();

    [Fact]
    public async Task GetAll_Returns200_WithCorrectPagedEnvelope_AndRoleItems()
    {
        var client = CreateClientWithFakeRoleService(FakeRoleService.WithSampleRoles());

        var response = await client.GetAsync("/api/v1/roles?pageNumber=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Roles retrieved successfully.", root.GetProperty("message").GetString());

        var data = root.GetProperty("data");
        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(1, data.GetProperty("pageNumber").GetInt32());
        Assert.Equal(10, data.GetProperty("pageSize").GetInt32());

        var items = data.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("ADMIN", items[0].GetProperty("roleCode").GetString());
        Assert.Equal("Administrator", items[0].GetProperty("roleName").GetString());
    }

    [Fact]
    public async Task GetById_Returns200_WithCorrectFields_AndNoUserOrPermissionCollections()
    {
        var client = CreateClientWithFakeRoleService(FakeRoleService.WithSampleRoles());

        var response = await client.GetAsync("/api/v1/roles/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");

        Assert.Equal(1, data.GetProperty("roleId").GetInt32());
        Assert.Equal("ADMIN", data.GetProperty("roleCode").GetString());
        Assert.Equal("Administrator", data.GetProperty("roleName").GetString());
        Assert.True(data.GetProperty("isSystemRole").GetBoolean());
        Assert.True(data.GetProperty("isActive").GetBoolean());

        // RoleDto is flat - assert no navigation-collection-shaped fields ever appear.
        Assert.False(data.TryGetProperty("users", out _));
        Assert.False(data.TryGetProperty("permissions", out _));
    }

    [Fact]
    public async Task GetById_Returns404_ThroughGlobalExceptionHandler_WhenRoleDoesNotExist()
    {
        var client = CreateClientWithFakeRoleService(FakeRoleService.WithSampleRoles());

        var response = await client.GetAsync("/api/v1/roles/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);

        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
    }

    private sealed class FakeRoleService : IRoleService
    {
        private readonly List<RoleDto> _roles;

        private FakeRoleService(List<RoleDto> roles)
        {
            _roles = roles;
        }

        public static FakeRoleService WithSampleRoles() => new(new List<RoleDto>
        {
            new()
            {
                RoleId = 1,
                RoleCode = "ADMIN",
                RoleName = "Administrator",
                Description = "Full access to all modules",
                IsSystemRole = true,
                IsActive = true,
            },
            new()
            {
                RoleId = 2,
                RoleCode = "MAINT_MANAGER",
                RoleName = "Maintenance Manager",
                Description = "Maintenance, reports and dashboard",
                IsSystemRole = false,
                IsActive = true,
            },
        });

        public Task<PagedResult<RoleDto>> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(PagedResult<RoleDto>.Create(_roles, request.PageNumber, request.PageSize, _roles.Count));

        public Task<RoleDto> GetByIdAsync(int roleId, CancellationToken cancellationToken)
        {
            var role = _roles.FirstOrDefault(r => r.RoleId == roleId);

            return role is null
                ? throw new NotFoundException(nameof(Role), roleId)
                : Task.FromResult(role);
        }
    }
}
