using System.Net;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// PUT /api/v1/roles/{id} through the real HTTP pipeline. Same harness as the create tests
/// (see RoleEndpointTests.cs): real RoleService, [RequirePermission], GlobalExceptionHandler and
/// model binding; only the persistence edge is faked. Harness roles: id 1 ADMIN (system role),
/// id 2 MAINT_MANAGER (ordinary role); the acting user is user id 1, "Test Admin".
/// </summary>
public partial class RoleEndpointTests
{
    private const string ValidUpdateBody =
        """{"roleCode":"MAINT_LEAD","roleName":"Maintenance Lead","description":"Leads maintenance"}""";

    private static async Task<JsonElement> ReadRootAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();

    [Fact]
    public async Task Put_Returns200_WithTheUpdatedRoleDto()
    {
        var h = CreateHarness();

        var response = await h.Client.PutAsync("/api/v1/roles/2", Json(ValidUpdateBody));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var root = await ReadRootAsync(response);
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Role updated successfully.", root.GetProperty("message").GetString());

        var data = root.GetProperty("data");
        Assert.Equal(2, data.GetProperty("roleId").GetInt32());
        Assert.Equal("MAINT_LEAD", data.GetProperty("roleCode").GetString());
        Assert.Equal("Maintenance Lead", data.GetProperty("roleName").GetString());
        Assert.Equal("Leads maintenance", data.GetProperty("description").GetString());
        Assert.False(data.GetProperty("isSystemRole").GetBoolean());
        Assert.True(data.GetProperty("isActive").GetBoolean());

        Assert.Equal("Maintenance Lead", h.Roles.ById(2).RoleName);
    }

    [Fact]
    public async Task Put_SetsUpdatedByFromTheJwtSubClaim()
    {
        var h = CreateHarness();

        await h.Client.PutAsync("/api/v1/roles/2", Json(ValidUpdateBody));

        Assert.Equal(1, h.Roles.ById(2).UpdatedBy);
        Assert.NotNull(h.Roles.ById(2).UpdatedAt);
    }

    [Fact]
    public async Task Put_RequiresAdminRolesEditPermission()
    {
        var h = CreateHarness();

        await h.Client.PutAsync("/api/v1/roles/2", Json(ValidUpdateBody));

        var check = Assert.Single(h.Authorization.Checks);
        Assert.Equal("ADMIN", check.RoleCode);
        Assert.Equal(ModuleCodes.AdminRoles, check.ModuleCode);
        Assert.Equal(PermissionAction.Edit, check.Action);
    }

    [Fact]
    public async Task Put_Returns401_WithoutToken_AndChangesNothing()
    {
        var h = CreateHarness(authenticate: false);

        var response = await h.Client.PutAsync("/api/v1/roles/2", Json(ValidUpdateBody));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("Maintenance Manager", h.Roles.ById(2).RoleName);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns403_WhenTheUserLacksAdminRolesEdit_AndChangesNothing()
    {
        var h = CreateHarness(permissionGranted: false);

        var response = await h.Client.PutAsync("/api/v1/roles/2", Json(ValidUpdateBody));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Maintenance Manager", h.Roles.ById(2).RoleName);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns404_ForAnUnknownRole()
    {
        var h = CreateHarness();

        var response = await h.Client.PutAsync("/api/v1/roles/999", Json(ValidUpdateBody));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await ReadRootAsync(response)).GetProperty("success").GetBoolean());
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns400_WithErrorList_ForInvalidInput()
    {
        var h = CreateHarness();

        var response = await h.Client.PutAsync("/api/v1/roles/2", Json("""{"roleCode":"BAD CODE","roleName":"  "}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var root = await ReadRootAsync(response);
        Assert.False(root.GetProperty("success").GetBoolean());
        var errors = root.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("RoleName is required.", errors);
        Assert.Contains(errors, e => e!.StartsWith("RoleCode may contain only"));
        Assert.Equal("Maintenance Manager", h.Roles.ById(2).RoleName);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns409_ForADuplicateRoleCode_WithoutLeakingDatabaseDetails()
    {
        var h = CreateHarness();

        var response = await h.Client.PutAsync("/api/v1/roles/2", Json(
            """{"roleCode":"admin","roleName":"Maintenance Manager"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.False(JsonDocument.Parse(body).RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("ADMIN", body);
        Assert.DoesNotContain("UQ_role_master", body);
        Assert.DoesNotContain("SqlException", body);
        Assert.Equal("MAINT_MANAGER", h.Roles.ById(2).RoleCode);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns409_ForADuplicateRoleName_IgnoringCase()
    {
        var h = CreateHarness();

        var response = await h.Client.PutAsync("/api/v1/roles/2", Json(
            """{"roleCode":"MAINT_MANAGER","roleName":"ADMINISTRATOR"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("Maintenance Manager", h.Roles.ById(2).RoleName);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns409_WhenTheRoleCodeOfASystemRoleIsChanged()
    {
        var h = CreateHarness();

        var response = await h.Client.PutAsync("/api/v1/roles/1", Json(
            """{"roleCode":"SUPER_ADMIN","roleName":"Administrator"}"""));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var root = await ReadRootAsync(response);
        Assert.Contains("system role", root.GetProperty("message").GetString());
        Assert.Equal("ADMIN", h.Roles.ById(1).RoleCode);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_AllowsEditingTheNameAndDescriptionOfASystemRole()
    {
        var h = CreateHarness();

        var response = await h.Client.PutAsync("/api/v1/roles/1", Json(
            """{"roleCode":"ADMIN","roleName":"Super Administrator","description":"Everything"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await ReadRootAsync(response)).GetProperty("data");
        Assert.Equal("Super Administrator", data.GetProperty("roleName").GetString());
        Assert.True(data.GetProperty("isSystemRole").GetBoolean());
        Assert.Equal("ADMIN", data.GetProperty("roleCode").GetString());
    }

    [Fact]
    public async Task Put_IgnoresClientSuppliedIsSystemRoleAndIsActive()
    {
        var h = CreateHarness();

        var response = await h.Client.PutAsync("/api/v1/roles/2", Json(
            """{"roleCode":"MAINT_MANAGER","roleName":"Maintenance Manager","isSystemRole":true,"isActive":false}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(h.Roles.ById(2).IsSystemRole);
        Assert.True(h.Roles.ById(2).IsActive);
    }

    [Fact]
    public async Task Put_Returns409_OnAConcurrentModification_WithoutLeakingDatabaseDetails()
    {
        var h = CreateHarness();
        h.Roles.ConflictOnUpdate = true; // what the repository throws when row_version no longer matches

        var response = await h.Client.PutAsync("/api/v1/roles/2", Json(ValidUpdateBody));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.False(JsonDocument.Parse(body).RootElement.GetProperty("success").GetBoolean());
        Assert.Contains("modified by another user", body);
        Assert.DoesNotContain("DbUpdateConcurrencyException", body);
        Assert.DoesNotContain("row_version", body);
        Assert.Equal("Maintenance Manager", h.Roles.ById(2).RoleName);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_WritesExactlyOneRoleUpdatedAuditEntry_ForTheAuthenticatedUser()
    {
        var h = CreateHarness();

        await h.Client.PutAsync("/api/v1/roles/2", Json(ValidUpdateBody));

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal("RoleUpdated", entry.Action);
        Assert.Equal("Security", entry.Module);
        Assert.Equal("Role", entry.EntityName);
        Assert.Equal(2, entry.EntityId);
        Assert.Equal("MAINT_LEAD", entry.RecordRef);
        Assert.Equal(1, entry.UserId);
        Assert.Equal("Test Admin", entry.UserName);
    }

    [Fact]
    public async Task Put_ThenGetById_ReturnsTheUpdatedRole()
    {
        var h = CreateHarness();

        await h.Client.PutAsync("/api/v1/roles/2", Json(ValidUpdateBody));
        var fetched = await h.Client.GetAsync("/api/v1/roles/2");

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        var data = (await ReadRootAsync(fetched)).GetProperty("data");
        Assert.Equal("MAINT_LEAD", data.GetProperty("roleCode").GetString());
        Assert.Equal("Maintenance Lead", data.GetProperty("roleName").GetString());
    }
}
