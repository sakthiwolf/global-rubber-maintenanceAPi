using System.Net;
using System.Text;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// /api/v1/maintenance-types through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model
/// binding) with the REAL MaintenanceTypeService. Only persistence is faked. Acting user: id 1 "Sakthi".
/// Types: 1 Oil &amp; Lubrication (Machine), 2 General Inspection (Both) - active; 3 Old Type (Mold) - inactive.
/// </summary>
public class MaintenanceTypeEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string Url = "/api/v1/maintenance-types";
    private readonly ApiWebApplicationFactory _factory;

    public MaintenanceTypeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemoryMaintenanceTypeRepository Types, RecordingAuditLog Audit, StubPermissionAuthorization Authorization);

    private H CreateHarness(bool permissionGranted = true, bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var types = new InMemoryMaintenanceTypeRepository(MaintenanceTypeTestData.MaintenanceTypes());
        var audit = new RecordingAuditLog();
        var authorization = decide is null ? new StubPermissionAuthorization(permissionGranted) : new StubPermissionAuthorization(decide);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMaintenanceTypeRepository>();
                services.AddSingleton<IMaintenanceTypeRepository>(types);
                services.RemoveAll<IUserRepository>();
                services.AddSingleton<IUserRepository>(users);
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

        return new H(client, types, audit, authorization);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Root(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static string Rv(H h, int id) => Convert.ToBase64String(h.Types.Stored(id).RowVersion);

    private static string CreateBody(string name = "Hydraulic System Service", string appliesTo = "Machine") =>
        JsonSerializer.Serialize(new { maintenanceTypeName = name, appliesTo });

    private static string UpdateBody(string rowVersion, string name = "General Inspection v2", string appliesTo = "Both") =>
        JsonSerializer.Serialize(new { maintenanceTypeName = name, appliesTo, rowVersion });

    // ================================================================ GET

    [Fact]
    public async Task GetAll_Returns200_WithThePagedEnvelope_AndHonoursTheFilters()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync($"{Url}?pageNumber=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Maintenance types retrieved successfully.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        var first = data.GetProperty("items")[0];
        Assert.Equal("MT-0002", first.GetProperty("maintenanceTypeCode").GetString());
        Assert.Equal("Both", first.GetProperty("appliesTo").GetString());
        Assert.False(string.IsNullOrEmpty(first.GetProperty("rowVersion").GetString()));

        async Task<int> Count(string qs) => (await Root(await h.Client.GetAsync(Url + qs))).GetProperty("data").GetProperty("totalCount").GetInt32();
        Assert.Equal(1, await Count("?search=lubric"));
        Assert.Equal(1, await Count("?appliesTo=Mold"));
        Assert.Equal(2, await Count("?isActive=true"));
    }

    [Fact]
    public async Task GetById_Returns200_And404_ForUnknown()
    {
        var h = CreateHarness();

        var data = (await Root(await h.Client.GetAsync($"{Url}/1"))).GetProperty("data");
        Assert.Equal("Oil & Lubrication", data.GetProperty("maintenanceTypeName").GetString());
        Assert.Equal("Machine", data.GetProperty("appliesTo").GetString());
        Assert.Equal(Rv(h, 1), data.GetProperty("rowVersion").GetString());

        var missing = await h.Client.GetAsync($"{Url}/999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.False((await Root(missing)).GetProperty("success").GetBoolean());
    }

    // ================================================================ POST

    [Fact]
    public async Task Post_Returns201_WithTheCode_AndALocationHeader_AndAudits()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync(Url, Json(CreateBody()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("MT-0004", data.GetProperty("maintenanceTypeCode").GetString());
        Assert.True(data.GetProperty("isActive").GetBoolean());
        Assert.Equal(4, h.Types.Count);
        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal("MaintenanceTypeCreated", entry.Action);
        Assert.Equal("Sakthi", entry.UserName);
    }

    [Fact]
    public async Task Post_IgnoresClientSuppliedServerControlledFields()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync(Url, Json(
            """{"maintenanceTypeName":"Belt Check","appliesTo":"Machine","maintenanceTypeId":99,"maintenanceTypeCode":"HACK","isActive":false,"createdBy":42,"rowVersion":"AAAA"}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = h.Types.Stored(4);
        Assert.Equal("MT-0004", stored.MaintenanceTypeCode);
        Assert.True(stored.IsActive);
        Assert.Equal(1, stored.CreatedBy);
    }

    [Fact]
    public async Task Post_Returns400_ForInvalidFields_And409_ForADuplicateActiveName_WritingNothing()
    {
        var h = CreateHarness();

        var invalid = await h.Client.PostAsync(Url, Json("""{"maintenanceTypeName":" ","appliesTo":"Tool"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await Root(invalid)).GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("MaintenanceTypeName is required.", errors);
        Assert.Contains("AppliesTo must be one of: Machine, Mold, Both.", errors);

        var duplicate = await h.Client.PostAsync(Url, Json(CreateBody(name: "OIL & LUBRICATION")));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var text = await duplicate.Content.ReadAsStringAsync();
        Assert.Contains("already exists", text);
        Assert.DoesNotContain("SqlException", text);

        Assert.Equal(3, h.Types.Count);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ PUT

    [Fact]
    public async Task Put_Returns200_WithAFreshRowVersion_AndNeverChangesCodeOrStatus()
    {
        var h = CreateHarness();
        var oldVersion = Rv(h, 2);

        var response = await h.Client.PutAsync($"{Url}/2", Json(UpdateBody(oldVersion).Replace("\"rowVersion\"", "\"maintenanceTypeCode\":\"HACK\",\"isActive\":false,\"rowVersion\"")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("General Inspection v2", data.GetProperty("maintenanceTypeName").GetString());
        Assert.NotEqual(oldVersion, data.GetProperty("rowVersion").GetString());
        Assert.Equal("MT-0002", h.Types.Stored(2).MaintenanceTypeCode);
        Assert.True(h.Types.Stored(2).IsActive);
        Assert.Equal("MaintenanceTypeUpdated", Assert.Single(h.Audit.Entries).Action);
    }

    [Fact]
    public async Task Put_Returns409_ForAStaleRowVersion_WithTheCleanMessage()
    {
        var h = CreateHarness();
        var stale = Rv(h, 2);
        h.Types.SimulateConcurrentModification(2);

        var response = await h.Client.PutAsync($"{Url}/2", Json(UpdateBody(stale)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("The maintenance type was modified by another user. Refresh the maintenance type and try again.", text);
        Assert.DoesNotContain("DbUpdateConcurrencyException", text);
        Assert.Equal("General Inspection", h.Types.Stored(2).MaintenanceTypeName);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns400_404_409_AsAppropriate()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsync($"{Url}/2", Json("""{"maintenanceTypeName":"A","appliesTo":"Both"}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PutAsync($"{Url}/999", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await h.Client.PutAsync($"{Url}/2", Json(UpdateBody(Rv(h, 2), name: "oil & lubrication")))).StatusCode);

        Assert.Equal("General Inspection", h.Types.Stored(2).MaintenanceTypeName);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ DELETE (soft deactivation)

    [Fact]
    public async Task Delete_Returns200_Deactivates_KeepsTheRow_AndAudits_Then409()
    {
        var h = CreateHarness();

        var response = await h.Client.DeleteAsync($"{Url}/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Maintenance type deactivated successfully.", root.GetProperty("message").GetString());
        Assert.False(root.GetProperty("data").GetProperty("isActive").GetBoolean());
        Assert.False(h.Types.Stored(1).IsActive);
        Assert.Equal(3, h.Types.Count);
        Assert.Equal("MaintenanceTypeDeactivated", Assert.Single(h.Audit.Entries).Action);

        var again = await h.Client.DeleteAsync($"{Url}/1");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already inactive", await again.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.DeleteAsync($"{Url}/999")).StatusCode);
        Assert.Single(h.Audit.Entries);
    }

    // ================================================================ authorization

    [Fact]
    public async Task Endpoints_Return401_WithoutAToken_AndChangeNothing()
    {
        var h = CreateHarness(authenticate: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync($"{Url}/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PostAsync(Url, Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PutAsync($"{Url}/2", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.DeleteAsync($"{Url}/2")).StatusCode);
        Assert.Equal(3, h.Types.Count);
        Assert.True(h.Types.Stored(2).IsActive);
        Assert.Empty(h.Authorization.Checks);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Endpoints_Return403_WithoutThePermission_AndChangeNothing()
    {
        var h = CreateHarness(permissionGranted: false);

        var post = await h.Client.PostAsync(Url, Json(CreateBody()));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync($"{Url}/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync($"{Url}/2", Json(UpdateBody(Rv(h, 2))))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync($"{Url}/2")).StatusCode);

        Assert.Equal("You do not have permission to perform this action.", (await Root(post)).GetProperty("message").GetString());
        Assert.Equal(3, h.Types.Count);
        Assert.Equal("General Inspection", h.Types.Stored(2).MaintenanceTypeName);
        Assert.True(h.Types.Stored(2).IsActive);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task EachEndpoint_AsksForTheMasterMaintenanceTypePermission_MatchingItsVerb()
    {
        var h = CreateHarness();

        await h.Client.GetAsync(Url);
        await h.Client.GetAsync($"{Url}/1");
        await h.Client.PostAsync(Url, Json(CreateBody()));
        await h.Client.PutAsync($"{Url}/2", Json(UpdateBody(Rv(h, 2))));
        await h.Client.DeleteAsync($"{Url}/2");

        Assert.Equal(
            new[] { PermissionAction.View, PermissionAction.View, PermissionAction.Add, PermissionAction.Edit, PermissionAction.Delete },
            h.Authorization.Checks.Select(c => c.Action));
        Assert.All(h.Authorization.Checks, c =>
        {
            Assert.Equal(ModuleCodes.MasterMaintenanceType, c.ModuleCode);
            Assert.Equal("ADMIN", c.RoleCode);
        });
    }

    [Fact]
    public async Task PermissionsAreDecidedPerAction_FromTheMatrix_NotTheRoleName()
    {
        // e.g. MAINT_MANAGER in the seed: View/Add/Edit but no Delete on MASTER_MAINTENANCE_TYPE. Even the ADMIN token is
        // refused Delete here, because the (stubbed) permission matrix says so.
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.MasterMaintenanceType && action != PermissionAction.Delete);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await h.Client.PostAsync(Url, Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsync($"{Url}/2", Json(UpdateBody(Rv(h, 2))))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync($"{Url}/2")).StatusCode);
        Assert.True(h.Types.Stored(2).IsActive);
    }

    [Fact]
    public async Task AnotherMastersPermission_DoesNotGrantMaintenanceTypeAccess()
    {
        var h = CreateHarness(decide: (_, module, _) => module is ModuleCodes.MasterBreakdownType or ModuleCodes.TrnMachinePm);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PostAsync(Url, Json(CreateBody()))).StatusCode);
        Assert.Equal(3, h.Types.Count);
    }
}
