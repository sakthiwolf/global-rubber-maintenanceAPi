using System.Net;
using System.Net.Http.Json;
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
/// Exercises GET/PUT /api/v1/roles/{roleId}/permissions through the real HTTP pipeline, with
/// <see cref="IPermissionService"/> swapped for an in-memory fake for the same reason as
/// <see cref="UserControllerTests"/>/<see cref="RoleControllerTests"/>: the real
/// PermissionService/PermissionRepository need a live SQL Server connection this environment
/// does not have.
/// </summary>
public class RolePermissionControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public RolePermissionControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    // The permission endpoints require AdminRoles.View/Edit: these tests are about the HTTP contract, so
    // they run as a signed-in user whose permission check is granted. 401/403 behavior is covered
    // separately in RoleAuthorizationTests.
    private HttpClient CreateClientWithFakePermissionService(FakePermissionService fake)
    {
        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IPermissionService>();
                services.AddSingleton<IPermissionService>(fake);
                services.RemoveAll<IPermissionAuthorizationService>();
                services.AddSingleton<IPermissionAuthorizationService>(new StubPermissionAuthorization(grantAll: true));
            });
        });

        var client = factory.CreateClient();
        TestAuth.Authenticate(client, factory);
        return client;
    }

    [Fact]
    public async Task GetPermissions_Returns200_WithFullMatrix_ForValidRole()
    {
        var client = CreateClientWithFakePermissionService(FakePermissionService.WithSampleMatrix());

        var response = await client.GetAsync("/api/v1/roles/2/permissions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");

        Assert.Equal(2, data.GetProperty("roleId").GetInt32());
        Assert.Equal("MAINT_MANAGER", data.GetProperty("roleCode").GetString());

        var permissions = data.GetProperty("permissions");
        Assert.Equal(2, permissions.GetArrayLength());

        var machine = permissions[0];
        Assert.Equal("MASTER_MACHINE", machine.GetProperty("moduleCode").GetString());
        Assert.True(machine.GetProperty("canView").GetBoolean());
        Assert.True(machine.GetProperty("canAdd").GetBoolean());
        Assert.False(machine.GetProperty("canDelete").GetBoolean());

        // A module with no permission_master row for this role still appears, all-false -
        // "missing row means no access", it is not silently created.
        var breakdown = permissions[1];
        Assert.Equal("TRN_MACHINE_BREAKDOWN", breakdown.GetProperty("moduleCode").GetString());
        Assert.False(breakdown.GetProperty("canView").GetBoolean());
        Assert.False(breakdown.GetProperty("canApprove").GetBoolean());
    }

    [Fact]
    public async Task GetPermissions_Returns404_ThroughGlobalExceptionHandler_WhenRoleDoesNotExist()
    {
        var client = CreateClientWithFakePermissionService(FakePermissionService.WithSampleMatrix());

        var response = await client.GetAsync("/api/v1/roles/999/permissions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task GetPermissions_ResponseContainsNoUnrelatedUserOrRoleNavigationData()
    {
        var client = CreateClientWithFakePermissionService(FakePermissionService.WithSampleMatrix());

        var response = await client.GetAsync("/api/v1/roles/2/permissions");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("loginId", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"users\"", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdatePermissions_Returns200_WithUpdatedMatrix()
    {
        var client = CreateClientWithFakePermissionService(FakePermissionService.WithSampleMatrix());

        var request = new UpdateRolePermissionsRequest
        {
            Permissions = new[]
            {
                new ModulePermissionUpdateDto
                {
                    ModuleId = 15,
                    CanView = true,
                    CanAdd = true,
                    CanEdit = true,
                    CanDelete = false,
                    CanApprove = true,
                    CanExport = true,
                },
            },
        };

        var response = await client.PutAsJsonAsync("/api/v1/roles/2/permissions", request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");

        var breakdown = data.GetProperty("permissions")[1];
        Assert.Equal("TRN_MACHINE_BREAKDOWN", breakdown.GetProperty("moduleCode").GetString());
        Assert.True(breakdown.GetProperty("canApprove").GetBoolean());
    }

    [Fact]
    public async Task UpdatePermissions_Returns400_ThroughGlobalExceptionHandler_ForInvalidModuleId()
    {
        var client = CreateClientWithFakePermissionService(FakePermissionService.WithSampleMatrix());

        var request = new UpdateRolePermissionsRequest
        {
            Permissions = new[]
            {
                new ModulePermissionUpdateDto { ModuleId = 999, CanView = true },
            },
        };

        var response = await client.PutAsJsonAsync("/api/v1/roles/2/permissions", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
    }

    private sealed class FakePermissionService : IPermissionService
    {
        private readonly Dictionary<int, List<ModulePermissionDto>> _matrixByRoleId;

        private FakePermissionService(Dictionary<int, List<ModulePermissionDto>> matrixByRoleId)
        {
            _matrixByRoleId = matrixByRoleId;
        }

        public static FakePermissionService WithSampleMatrix() => new(new Dictionary<int, List<ModulePermissionDto>>
        {
            [2] = new List<ModulePermissionDto>
            {
                new()
                {
                    ModuleId = 2,
                    ModuleCode = "MASTER_MACHINE",
                    ModuleName = "Machine",
                    MenuGroup = "Masters",
                    SortOrder = 10,
                    CanView = true,
                    CanAdd = true,
                    CanEdit = true,
                    CanDelete = false,
                    CanApprove = false,
                    CanExport = true,
                },
                new()
                {
                    ModuleId = 15,
                    ModuleCode = "TRN_MACHINE_BREAKDOWN",
                    ModuleName = "Machine Breakdown",
                    MenuGroup = "Transactions",
                    SortOrder = 23,
                    // No permission_master row for this role/module - all false by design.
                    CanView = false,
                    CanAdd = false,
                    CanEdit = false,
                    CanDelete = false,
                    CanApprove = false,
                    CanExport = false,
                },
            },
        });

        public Task<RolePermissionMatrixDto> GetRolePermissionsAsync(int roleId, CancellationToken cancellationToken)
        {
            if (!_matrixByRoleId.TryGetValue(roleId, out var permissions))
            {
                throw new NotFoundException(nameof(Role), roleId);
            }

            return Task.FromResult(new RolePermissionMatrixDto
            {
                RoleId = roleId,
                RoleCode = "MAINT_MANAGER",
                RoleName = "Maintenance Manager",
                Permissions = permissions,
            });
        }

        public Task<RolePermissionMatrixDto> GetMyPermissionsAsync(int userId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by RolePermissionControllerTests.");

        public Task<RolePermissionMatrixDto> UpdateRolePermissionsAsync(
            int roleId, UpdateRolePermissionsRequest request, int? actingUserId, string? ipAddress,
            CancellationToken cancellationToken)
        {
            if (!_matrixByRoleId.TryGetValue(roleId, out var permissions))
            {
                throw new NotFoundException(nameof(Role), roleId);
            }

            var validModuleIds = permissions.Select(p => p.ModuleId).ToHashSet();
            var unknownModuleIds = request.Permissions.Select(u => u.ModuleId).Where(id => !validModuleIds.Contains(id)).ToList();

            if (unknownModuleIds.Count > 0)
            {
                throw new ValidationException($"Unknown or inactive moduleId(s): {string.Join(", ", unknownModuleIds)}.");
            }

            var updated = permissions
                .Select(p =>
                {
                    var update = request.Permissions.FirstOrDefault(u => u.ModuleId == p.ModuleId);
                    return update is null
                        ? p
                        : new ModulePermissionDto
                        {
                            ModuleId = p.ModuleId,
                            ModuleCode = p.ModuleCode,
                            ModuleName = p.ModuleName,
                            MenuGroup = p.MenuGroup,
                            SortOrder = p.SortOrder,
                            CanView = update.CanView,
                            CanAdd = update.CanAdd,
                            CanEdit = update.CanEdit,
                            CanDelete = update.CanDelete,
                            CanApprove = update.CanApprove,
                            CanExport = update.CanExport,
                        };
                })
                .ToList();

            _matrixByRoleId[roleId] = updated;

            return Task.FromResult(new RolePermissionMatrixDto
            {
                RoleId = roleId,
                RoleCode = "MAINT_MANAGER",
                RoleName = "Maintenance Manager",
                Permissions = updated,
            });
        }
    }
}
