using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// DELETE /api/v1/roles/{id} (soft deactivation) through the real HTTP pipeline. Same harness as
/// the create/update tests (RoleEndpointTests.cs). Harness roles: id 1 ADMIN (system role),
/// id 2 MAINT_MANAGER (ordinary, active); the acting user is user id 1, "Test Admin".
/// </summary>
public partial class RoleEndpointTests
{
    [Fact]
    public async Task Delete_Returns200_WithTheDeactivatedRole_AndKeepsTheRow()
    {
        var h = CreateHarness();

        var response = await h.Client.DeleteAsync("/api/v1/roles/2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = await ReadRootAsync(response);
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Role deactivated successfully.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(2, data.GetProperty("roleId").GetInt32());
        Assert.False(data.GetProperty("isActive").GetBoolean());

        // Soft: the row is still there, only inactive, and everything else is as it was.
        var stored = h.Roles.ById(2);
        Assert.False(stored.IsActive);
        Assert.Equal("MAINT_MANAGER", stored.RoleCode);
        Assert.Equal("Maintenance Manager", stored.RoleName);
        Assert.False(stored.IsSystemRole);
        Assert.Equal(2, h.Roles.Count);
    }

    [Fact]
    public async Task Delete_ThenGetById_StillReturnsTheRole_AsInactive()
    {
        var h = CreateHarness();

        await h.Client.DeleteAsync("/api/v1/roles/2");
        var fetched = await h.Client.GetAsync("/api/v1/roles/2");

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var data = (await ReadRootAsync(fetched)).GetProperty("data");
        Assert.Equal("MAINT_MANAGER", data.GetProperty("roleCode").GetString());
        Assert.False(data.GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Delete_SetsUpdatedByFromTheJwtSubClaim_AndUpdatedAt()
    {
        var h = CreateHarness();

        await h.Client.DeleteAsync("/api/v1/roles/2");

        Assert.Equal(1, h.Roles.ById(2).UpdatedBy);
        Assert.NotNull(h.Roles.ById(2).UpdatedAt);
    }

    [Fact]
    public async Task Delete_RequiresAdminRolesDeletePermission()
    {
        var h = CreateHarness();

        await h.Client.DeleteAsync("/api/v1/roles/2");

        var check = Assert.Single(h.Authorization.Checks);
        Assert.Equal("ADMIN", check.RoleCode);
        Assert.Equal(ModuleCodes.AdminRoles, check.ModuleCode);
        Assert.Equal(PermissionAction.Delete, check.Action);
    }

    [Fact]
    public async Task Delete_Returns401_WithoutToken_AndChangesNothing()
    {
        var h = CreateHarness(authenticate: false);

        var response = await h.Client.DeleteAsync("/api/v1/roles/2");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.True(h.Roles.ById(2).IsActive);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Delete_Returns403_WhenTheUserLacksAdminRolesDelete_AndChangesNothing()
    {
        var h = CreateHarness(permissionGranted: false);

        var response = await h.Client.DeleteAsync("/api/v1/roles/2");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(h.Roles.ById(2).IsActive);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Delete_Returns404_ForAnUnknownRole()
    {
        var h = CreateHarness();

        var response = await h.Client.DeleteAsync("/api/v1/roles/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await ReadRootAsync(response)).GetProperty("success").GetBoolean());
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Delete_Returns409_ForASystemRole_AndChangesNothing()
    {
        var h = CreateHarness();

        var response = await h.Client.DeleteAsync("/api/v1/roles/1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("system role", (await ReadRootAsync(response)).GetProperty("message").GetString());
        Assert.True(h.Roles.ById(1).IsActive);
        Assert.Null(h.Roles.ById(1).UpdatedBy);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Delete_Returns409_WhileActiveUsersAreAssigned_WithoutExposingUserDetails()
    {
        var h = CreateHarness();
        h.Users.RoleIdsWithActiveUsers.Add(2);

        var response = await h.Client.DeleteAsync("/api/v1/roles/2");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("active users are assigned", body);
        Assert.DoesNotContain("Test Admin", body);
        Assert.True(h.Roles.ById(2).IsActive);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Delete_Returns409_ForAnAlreadyInactiveRole_AndAuditsNothing()
    {
        var h = CreateHarness();

        var first = await h.Client.DeleteAsync("/api/v1/roles/2");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        h.Audit.Entries.Clear();

        var second = await h.Client.DeleteAsync("/api/v1/roles/2");

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("The role is already inactive.", (await ReadRootAsync(second)).GetProperty("message").GetString());
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Delete_Returns409_OnAConcurrentModification_WithoutLeakingDatabaseDetails()
    {
        var h = CreateHarness();
        h.Roles.ConflictOnUpdate = true; // what the repository throws when row_version no longer matches

        var response = await h.Client.DeleteAsync("/api/v1/roles/2");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("modified by another user", body);
        Assert.DoesNotContain("DbUpdateConcurrencyException", body);
        Assert.DoesNotContain("row_version", body);
        Assert.True(h.Roles.ById(2).IsActive);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Delete_WritesExactlyOneRoleDeactivatedAuditEntry_ForTheAuthenticatedUser()
    {
        var h = CreateHarness();

        await h.Client.DeleteAsync("/api/v1/roles/2");

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal("RoleDeactivated", entry.Action);
        Assert.Equal("Security", entry.Module);
        Assert.Equal("Role", entry.EntityName);
        Assert.Equal(2, entry.EntityId);
        Assert.Equal("MAINT_MANAGER", entry.RecordRef);
        Assert.Equal(1, entry.UserId);
        Assert.Equal("Test Admin", entry.UserName);
    }

    [Fact]
    public async Task Delete_LeavesThePermissionMatrixIntact()
    {
        var h = CreateHarness();

        // Give the role some permissions first.
        var saved = await h.Client.PutAsJsonAsync("/api/v1/roles/2/permissions", new UpdateRolePermissionsRequest
        {
            Permissions = new[]
            {
                new ModulePermissionUpdateDto { ModuleId = 10, CanView = true, CanAdd = true, CanEdit = true, CanExport = true },
                new ModulePermissionUpdateDto { ModuleId = 11, CanView = true, CanExport = true },
            },
        });
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var matrixBefore = (await GetMatrixJsonAsync(h.Client, 2));
        Assert.Equal(2, h.Permissions.RowCountForRole(2));

        var deleted = await h.Client.DeleteAsync("/api/v1/roles/2");
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        // No permission row was removed or altered: the same rows, and the identical matrix - which
        // is also exactly what a later reactivation would find.
        Assert.Equal(2, h.Permissions.RowCountForRole(2));
        Assert.Equal(matrixBefore, await GetMatrixJsonAsync(h.Client, 2));
    }

    private static async Task<string> GetMatrixJsonAsync(HttpClient client, int roleId)
    {
        var response = await client.GetAsync($"/api/v1/roles/{roleId}/permissions");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var permissions = JsonDocument.Parse(await response.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("permissions");
        return permissions.GetRawText();
    }
}
