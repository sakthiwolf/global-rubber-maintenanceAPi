using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// PUT /api/v1/roles/{id}/permissions and GET /api/v1/auth/me/permissions through the real HTTP
/// pipeline with the REAL PermissionService (same harness as RoleEndpointTests.cs). Harness modules:
/// id 10 MASTER_MACHINE, id 11 MASTER_MOLD; roles: id 1 ADMIN, id 2 MAINT_MANAGER; users: 1 = Test Admin (role 1),
/// 2 = Test Manager (role 2).
/// </summary>
public partial class RoleEndpointTests
{
    private static Task<HttpResponseMessage> PutPermissionsAsync(HttpClient client, int roleId, params ModulePermissionUpdateDto[] updates) =>
        client.PutAsJsonAsync($"/api/v1/roles/{roleId}/permissions", new UpdateRolePermissionsRequest { Permissions = updates });

    [Fact]
    public async Task PutPermissions_WritesOnePermissionsUpdatedEntry_WithOneDetailRowPerChangedFlag()
    {
        var h = CreateHarness();

        var response = await PutPermissionsAsync(h.Client, 2,
            new ModulePermissionUpdateDto { ModuleId = 10, CanView = true, CanAdd = true, CanExport = true },
            new ModulePermissionUpdateDto { ModuleId = 11, CanView = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal("PermissionsUpdated", entry.Action);
        Assert.Equal("Security", entry.Module);
        Assert.Equal("Role", entry.EntityName);
        Assert.Equal(2, entry.EntityId);
        Assert.Equal("MAINT_MANAGER", entry.RecordRef);
        Assert.Equal(1, entry.UserId); // JWT "sub"
        Assert.Equal("Test Admin", entry.UserName);

        Assert.Equal(
            new[]
            {
                new AuditLogDetailEntry("MASTER_MACHINE.can_view", "false", "true"),
                new AuditLogDetailEntry("MASTER_MACHINE.can_add", "false", "true"),
                new AuditLogDetailEntry("MASTER_MACHINE.can_export", "false", "true"),
                new AuditLogDetailEntry("MASTER_MOLD.can_view", "false", "true"),
            },
            entry.Details.ToArray());
    }

    [Fact]
    public async Task PutPermissions_OnlyTheChangedFlagsAreAudited_OnASecondSave()
    {
        var h = CreateHarness();
        await PutPermissionsAsync(h.Client, 2,
            new ModulePermissionUpdateDto { ModuleId = 10, CanView = true, CanAdd = true, CanExport = true });
        h.Audit.Entries.Clear();

        // Same matrix again, except Export is switched off.
        await PutPermissionsAsync(h.Client, 2,
            new ModulePermissionUpdateDto { ModuleId = 10, CanView = true, CanAdd = true, CanExport = false });

        var detail = Assert.Single(Assert.Single(h.Audit.Entries).Details);
        Assert.Equal(new AuditLogDetailEntry("MASTER_MACHINE.can_export", "true", "false"), detail);
    }

    [Fact]
    public async Task PutPermissions_SavingIdenticalValues_ProducesNoDetailRows()
    {
        var h = CreateHarness();
        var update = new ModulePermissionUpdateDto { ModuleId = 10, CanView = true };
        await PutPermissionsAsync(h.Client, 2, update);
        h.Audit.Entries.Clear();

        await PutPermissionsAsync(h.Client, 2, update);

        Assert.Empty(Assert.Single(h.Audit.Entries).Details);
    }

    [Fact]
    public async Task PutPermissions_PersistsAllSixFlags_AndStampsUpdatedByFromTheToken()
    {
        var h = CreateHarness();

        await PutPermissionsAsync(h.Client, 2, new ModulePermissionUpdateDto
        {
            ModuleId = 10, CanView = true, CanAdd = true, CanEdit = true, CanDelete = true, CanApprove = true, CanExport = true,
        });

        var row = Assert.Single(h.Permissions.RowsForRole(2));
        Assert.True(row.CanView && row.CanAdd && row.CanEdit && row.CanDelete && row.CanApprove && row.CanExport);
        Assert.Equal(1, row.UpdatedBy);
        Assert.Equal(1, row.CreatedBy);
    }

    [Fact]
    public async Task PutPermissions_WithAnUnknownModule_Returns400_AndWritesNothingAndNoAudit()
    {
        var h = CreateHarness();

        var response = await PutPermissionsAsync(h.Client, 2, new ModulePermissionUpdateDto { ModuleId = 999, CanView = true });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(h.Permissions.RowsForRole(2));
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task PutPermissions_ForAnUnknownRole_Returns404_AndNoAudit()
    {
        var h = CreateHarness();

        var response = await PutPermissionsAsync(h.Client, 999, new ModulePermissionUpdateDto { ModuleId = 10, CanView = true });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task PutPermissions_WithoutTheEditPermission_Returns403_AndChangesAndAuditsNothing()
    {
        var h = CreateHarness(permissionGranted: false);

        var response = await PutPermissionsAsync(h.Client, 2, new ModulePermissionUpdateDto { ModuleId = 10, CanView = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(h.Permissions.RowsForRole(2));
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task PutPermissions_WithoutAToken_Returns401_AndChangesAndAuditsNothing()
    {
        var h = CreateHarness(authenticate: false);

        var response = await PutPermissionsAsync(h.Client, 2, new ModulePermissionUpdateDto { ModuleId = 10, CanView = true });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(h.Permissions.RowsForRole(2));
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task GetPermissions_ForARoleWithNoRows_ReturnsEveryActiveModuleAsAllFalse()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync("/api/v1/roles/2/permissions");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var permissions = (await ReadRootAsync(response)).GetProperty("data").GetProperty("permissions");
        Assert.Equal(2, permissions.GetArrayLength());
        foreach (var module in permissions.EnumerateArray())
        {
            foreach (var flag in new[] { "canView", "canAdd", "canEdit", "canDelete", "canApprove", "canExport" })
            {
                Assert.False(module.GetProperty(flag).GetBoolean());
            }
        }
    }

    // ---- GET /auth/me/permissions: the caller's own matrix, resolved from THEIR user record ----

    // Sends the request as another user by overriding the Authorization header on that one request.
    private Task<HttpResponseMessage> GetMyPermissionsAs(Harness h, int tokenUserId, string tokenRoleCode)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me/permissions");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", TestAuth.MintToken(_factory, tokenRoleCode, tokenUserId));

        return h.Client.SendAsync(request);
    }

    [Fact]
    public async Task MyPermissions_ReturnsTheMatrixOfTheTokensUsersOwnRole_NotAnyOther()
    {
        var h = CreateHarness();
        await PutPermissionsAsync(h.Client, 2, new ModulePermissionUpdateDto { ModuleId = 10, CanView = true }); // role 2 gets Machine.View
        var checksBefore = h.Authorization.Checks.Count;

        var manager = await GetMyPermissionsAs(h, tokenUserId: 2, tokenRoleCode: "MAINT_MANAGER"); // user 2 has role 2
        Assert.Equal(HttpStatusCode.OK, manager.StatusCode);
        var managerData = (await ReadRootAsync(manager)).GetProperty("data");
        Assert.Equal(2, managerData.GetProperty("roleId").GetInt32());
        Assert.True(managerData.GetProperty("permissions").EnumerateArray()
            .Single(m => m.GetProperty("moduleId").GetInt32() == 10).GetProperty("canView").GetBoolean());

        var admin = await GetMyPermissionsAs(h, tokenUserId: 1, tokenRoleCode: "ADMIN"); // user 1 has role 1 - no rows
        var adminData = (await ReadRootAsync(admin)).GetProperty("data");
        Assert.Equal(1, adminData.GetProperty("roleId").GetInt32());
        Assert.False(adminData.GetProperty("permissions").EnumerateArray()
            .Single(m => m.GetProperty("moduleId").GetInt32() == 10).GetProperty("canView").GetBoolean());

        // Neither call consulted permission_master: no AdminRoles permission is needed to read one's own matrix.
        Assert.Equal(checksBefore, h.Authorization.Checks.Count);
    }

    [Fact]
    public async Task MyPermissions_WorksEvenWhenEveryAdminRolesPermissionIsDenied()
    {
        var h = CreateHarness(permissionGranted: false);

        var response = await GetMyPermissionsAs(h, tokenUserId: 2, tokenRoleCode: "PRODUCTION_USER");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(h.Authorization.Checks);
    }

    [Fact]
    public async Task MyPermissions_Returns401_WithoutAToken()
    {
        var h = CreateHarness(authenticate: false);

        var response = await h.Client.GetAsync("/api/v1/auth/me/permissions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MyPermissions_Returns401_ForATokenWhoseUserNoLongerExists()
    {
        var h = CreateHarness(tokenUserId: 999);

        var response = await h.Client.GetAsync("/api/v1/auth/me/permissions");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
