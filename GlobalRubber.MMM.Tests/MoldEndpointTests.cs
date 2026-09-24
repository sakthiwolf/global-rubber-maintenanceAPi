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
/// /api/v1/molds through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model binding) with the
/// REAL MoldService. Only persistence is faked. Acting user: id 1 "Sakthi". See <see cref="MoldTestData"/>.
/// </summary>
public class MoldEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public MoldEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemoryMoldRepository Molds, RecordingAuditLog Audit, StubPermissionAuthorization Authorization);

    private H CreateHarness(bool permissionGranted = true, bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var products = new InMemoryProductRepository(ProductTestData.Products());
        var molds = new InMemoryMoldRepository(MoldTestData.Molds(), products, employees);
        var audit = new RecordingAuditLog();
        var authorization = decide is null ? new StubPermissionAuthorization(permissionGranted) : new StubPermissionAuthorization(decide);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMoldRepository>();
                services.AddSingleton<IMoldRepository>(molds);
                services.RemoveAll<IProductRepository>();
                services.AddSingleton<IProductRepository>(products);
                services.RemoveAll<IEmployeeRepository>();
                services.AddSingleton<IEmployeeRepository>(employees);
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

        return new H(client, molds, audit, authorization);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Root(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static string Rv(H h, int id) => Convert.ToBase64String(h.Molds.Stored(id).RowVersion);
    private static List<string?> Errors(JsonElement root) => root.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();

    private static string CreateBody(string name = "Bush Mold C", int productId = 2, int? personId = 2, string? serial = "BM-9", int max = 500000, int warning = 450000, int replacement = 500000) =>
        JsonSerializer.Serialize(new
        {
            moldName = name, productId, moldType = "Injection", cavityCount = 2, manufacturer = "Precision", serialNumber = serial,
            location = "MAC-0002", storageLocation = "Rack B-2", commissionDate = "2024-06-01", maximumShots = max, warningShots = warning,
            replacementShots = replacement, maintenanceFrequencyShots = 50000, responsibleEmployeeId = personId, status = "Available", remarks = "New",
        });

    private static string UpdateBody(string rowVersion, string name = "Seal Mold A2", int productId = 1, int? usage = 100000, string status = "In Production") =>
        JsonSerializer.Serialize(new
        {
            moldName = name, productId, moldType = "Compression", cavityCount = 4, serialNumber = "SM-1", maximumShots = 500000, warningShots = 450000,
            replacementShots = 500000, currentUsageShots = usage, responsibleEmployeeId = 1, status, rowVersion,
        });

    // ================================================================ GET

    [Fact]
    public async Task GetAll_Returns200_WithThePagedEnvelope_AndLifeValues()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync("/api/v1/molds?pageNumber=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Molds retrieved successfully.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        var seal = data.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("moldCode").GetString() == "MLD-0001");
        Assert.Equal("Rubber Seal A", seal.GetProperty("productName").GetString());
        Assert.Equal("Normal", seal.GetProperty("lifeState").GetString());
        Assert.Equal(20, seal.GetProperty("lifeUsedPercent").GetInt32());
        Assert.Equal(400000, seal.GetProperty("remainingShots").GetInt32());
        Assert.Equal("2022-01-10", seal.GetProperty("commissionDate").GetString());
        Assert.False(seal.TryGetProperty("isActive", out _));
        Assert.False(string.IsNullOrEmpty(seal.GetProperty("rowVersion").GetString()));
    }

    [Fact]
    public async Task GetAll_HonoursTheFilters_And404ForAnUnknownId()
    {
        var h = CreateHarness();
        async Task<int> Count(string qs) => (await Root(await h.Client.GetAsync("/api/v1/molds" + qs))).GetProperty("data").GetProperty("totalCount").GetInt32();

        Assert.Equal(1, await Count("?search=gasket"));
        Assert.Equal(1, await Count("?status=Retired"));
        Assert.Equal(1, await Count("?productId=3"));
        Assert.Equal(1, await Count("?lifeState=Warning"));

        var missing = await h.Client.GetAsync("/api/v1/molds/999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.False((await Root(missing)).GetProperty("success").GetBoolean());
    }

    // ================================================================ POST

    [Fact]
    public async Task Post_Returns201_WithTheCodeAndZeroUsage_AndAudits()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/molds", Json(CreateBody()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("MLD-0004", data.GetProperty("moldCode").GetString());
        Assert.Equal(0, data.GetProperty("currentUsageShots").GetInt32());
        Assert.Equal("Normal", data.GetProperty("lifeState").GetString());
        Assert.Equal(4, h.Molds.Count);
        Assert.Equal("MoldCreated", Assert.Single(h.Audit.Entries).Action);
    }

    [Fact]
    public async Task Post_IgnoresSystemManagedFields_IncludingUsageAndLifeState()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/molds", Json(CreateBody().Replace("\"status\"",
            "\"moldId\":99,\"moldCode\":\"HACK\",\"currentUsageShots\":480000,\"lifeState\":\"Replace\",\"createdBy\":42,\"rowVersion\":\"AAAA\",\"status\"")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = h.Molds.Stored(4);
        Assert.Equal("MLD-0004", stored.MoldCode);
        Assert.Equal(0, stored.CurrentUsageShots);
        Assert.Equal("Normal", stored.LifeState);
        Assert.Equal(1, stored.CreatedBy);
    }

    [Fact]
    public async Task Post_Returns400_404_409_AsAppropriate_WritingNothing()
    {
        var h = CreateHarness();

        var invalid = await h.Client.PostAsync("/api/v1/molds", Json("""{"moldName":" ","productId":0,"cavityCount":0,"maximumShots":1000,"warningShots":1000,"replacementShots":1200,"status":"Lost"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = Errors(await Root(invalid));
        foreach (var expected in new[] { "MoldName is required.", "ProductId is required.", "MoldType is required.", "CavityCount must be at least 1.", "Warning level must be less than maximum life.", "Replacement level cannot exceed maximum shots.", "Status must be one of: Available, In Production, Maintenance, Replacement Due, Retired." })
        {
            Assert.Contains(expected, errors);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PostAsync("/api/v1/molds", Json(CreateBody(productId: 999)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PostAsync("/api/v1/molds", Json(CreateBody(personId: 999)))).StatusCode);
        var inactiveProduct = await h.Client.PostAsync("/api/v1/molds", Json(CreateBody(productId: 3)));
        Assert.Equal(HttpStatusCode.BadRequest, inactiveProduct.StatusCode);
        Assert.Contains("The selected product is not active.", Errors(await Root(inactiveProduct)));
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PostAsync("/api/v1/molds", Json(CreateBody(personId: 3)))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await h.Client.PostAsync("/api/v1/molds", Json(CreateBody(name: "SEAL MOLD A")))).StatusCode);
        var serial = await h.Client.PostAsync("/api/v1/molds", Json(CreateBody(serial: "SM-1")));
        Assert.Equal(HttpStatusCode.Conflict, serial.StatusCode);
        Assert.DoesNotContain("UX_mold_master", await serial.Content.ReadAsStringAsync());

        Assert.Equal(3, h.Molds.Count);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ PUT

    [Fact]
    public async Task Put_Returns200_AppliesTheUsageCorrection_AndReturnsAFreshRowVersion()
    {
        var h = CreateHarness();
        var oldVersion = Rv(h, 1);

        var response = await h.Client.PutAsync("/api/v1/molds/1", Json(UpdateBody(oldVersion, usage: 500000, status: "Replacement Due")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("Seal Mold A2", data.GetProperty("moldName").GetString());
        Assert.Equal(500000, data.GetProperty("currentUsageShots").GetInt32());
        Assert.Equal("Replace", data.GetProperty("lifeState").GetString());
        Assert.Equal("Replacement Due", data.GetProperty("status").GetString());
        Assert.Equal("MLD-0001", data.GetProperty("moldCode").GetString());
        Assert.NotEqual(oldVersion, data.GetProperty("rowVersion").GetString());
        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal("MoldUpdated", entry.Action);
        Assert.Contains(entry.Details, d => d is { FieldName: "current_usage_shots", OldValue: "100000", NewValue: "500000" });
    }

    [Fact]
    public async Task Put_Returns409_ForAStaleRowVersion_WithTheCleanMessage()
    {
        var h = CreateHarness();
        var stale = Rv(h, 1);
        h.Molds.SimulateConcurrentModification(1);

        var response = await h.Client.PutAsync("/api/v1/molds/1", Json(UpdateBody(stale)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("The mold was modified by another user. Refresh the mold and try again.", text);
        Assert.DoesNotContain("DbUpdateConcurrencyException", text);
        Assert.Equal("Seal Mold A", h.Molds.Stored(1).MoldName);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns400_404_409_AsAppropriate()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsync("/api/v1/molds/1", Json(UpdateBody(Rv(h, 1)).Replace($"\"rowVersion\":\"{Rv(h, 1)}\"", "\"rowVersion\":\"\"")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsync("/api/v1/molds/1", Json(UpdateBody(Rv(h, 1), usage: -5)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PutAsync("/api/v1/molds/999", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsync("/api/v1/molds/1", Json(UpdateBody(Rv(h, 1), productId: 3)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await h.Client.PutAsync("/api/v1/molds/1", Json(UpdateBody(Rv(h, 1), name: "gasket mold")))).StatusCode);

        Assert.Equal("Seal Mold A", h.Molds.Stored(1).MoldName);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ DELETE (= retire)

    [Fact]
    public async Task Delete_Returns200_RetiresTheMold_KeepsTheRow_AndAudits_Then409()
    {
        var h = CreateHarness();

        var response = await h.Client.DeleteAsync("/api/v1/molds/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Mold retired successfully.", root.GetProperty("message").GetString());
        Assert.Equal("Retired", root.GetProperty("data").GetProperty("status").GetString());
        Assert.Equal("Retired", h.Molds.Stored(1).Status);
        Assert.Equal(3, h.Molds.Count);
        Assert.Equal("MoldDeactivated", Assert.Single(h.Audit.Entries).Action);

        var again = await h.Client.DeleteAsync("/api/v1/molds/1");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already retired", await again.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.DeleteAsync("/api/v1/molds/999")).StatusCode);
        Assert.Single(h.Audit.Entries);
    }

    // ================================================================ authorization

    [Fact]
    public async Task Molds_Endpoints_Return401_WithoutAToken_AndChangeNothing()
    {
        var h = CreateHarness(authenticate: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/molds")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/molds/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PostAsync("/api/v1/molds", Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PutAsync("/api/v1/molds/1", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.DeleteAsync("/api/v1/molds/1")).StatusCode);
        Assert.Equal(3, h.Molds.Count);
        Assert.Equal("In Production", h.Molds.Stored(1).Status);
        Assert.Empty(h.Authorization.Checks);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Molds_Endpoints_Return403_WithoutThePermission_AndChangeNothing()
    {
        var h = CreateHarness(permissionGranted: false);

        var post = await h.Client.PostAsync("/api/v1/molds", Json(CreateBody()));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/molds")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/molds/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync("/api/v1/molds/1", Json(UpdateBody(Rv(h, 1))))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync("/api/v1/molds/1")).StatusCode);

        Assert.Equal("You do not have permission to perform this action.", (await Root(post)).GetProperty("message").GetString());
        Assert.Equal(3, h.Molds.Count);
        Assert.Equal("Seal Mold A", h.Molds.Stored(1).MoldName);
        Assert.Equal("In Production", h.Molds.Stored(1).Status);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task EachEndpoint_AsksForTheMasterMoldPermission_MatchingItsVerb()
    {
        var h = CreateHarness();

        await h.Client.GetAsync("/api/v1/molds");
        await h.Client.GetAsync("/api/v1/molds/1");
        await h.Client.PostAsync("/api/v1/molds", Json(CreateBody()));
        await h.Client.PutAsync("/api/v1/molds/1", Json(UpdateBody(Rv(h, 1))));
        await h.Client.DeleteAsync("/api/v1/molds/1");

        Assert.Equal(
            new[] { PermissionAction.View, PermissionAction.View, PermissionAction.Add, PermissionAction.Edit, PermissionAction.Delete },
            h.Authorization.Checks.Select(c => c.Action));
        Assert.All(h.Authorization.Checks, c =>
        {
            Assert.Equal(ModuleCodes.MasterMold, c.ModuleCode);
            Assert.Equal("ADMIN", c.RoleCode);
        });
    }

    [Fact]
    public async Task ViewOnly_CanReadButNotWrite_DecidedByTheMatrixNotTheRoleName()
    {
        // e.g. PRODUCTION_USER / MAINT_ENGINEER in the seed: Mold View only. Even the ADMIN token is refused writes here.
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.MasterMold && action == PermissionAction.View);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync("/api/v1/molds")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync("/api/v1/molds/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PostAsync("/api/v1/molds", Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync("/api/v1/molds/1", Json(UpdateBody(Rv(h, 1))))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync("/api/v1/molds/1")).StatusCode);
        Assert.Equal(3, h.Molds.Count);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task ProductOrMachinePermissions_DoNotGrantMoldAccess()
    {
        var h = CreateHarness(decide: (_, module, _) => module is ModuleCodes.MasterProduct or ModuleCodes.MasterMachine);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/molds")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PostAsync("/api/v1/molds", Json(CreateBody()))).StatusCode);
        Assert.Equal(3, h.Molds.Count);
    }
}
