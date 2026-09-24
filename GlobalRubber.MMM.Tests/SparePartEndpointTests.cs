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
/// /api/v1/spare-parts through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model binding) with
/// the REAL SparePartService. Only persistence is faked. Acting user: id 1 "Sakthi". See <see cref="SparePartTestData"/>.
/// </summary>
public class SparePartEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public SparePartEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemorySparePartRepository Parts, RecordingAuditLog Audit, StubPermissionAuthorization Authorization);

    private H CreateHarness(bool permissionGranted = true, bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machines = new InMemoryMachineRepository(MachineTestData.Machines(), departments, employees);
        var vendors = new InMemoryVendorRepository(VendorTestData.Vendors());
        var parts = new InMemorySparePartRepository(SparePartTestData.SpareParts(), machines, vendors);
        var audit = new RecordingAuditLog();
        var authorization = decide is null ? new StubPermissionAuthorization(permissionGranted) : new StubPermissionAuthorization(decide);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISparePartRepository>();
                services.AddSingleton<ISparePartRepository>(parts);
                services.RemoveAll<IMachineRepository>();
                services.AddSingleton<IMachineRepository>(machines);
                services.RemoveAll<IVendorRepository>();
                services.AddSingleton<IVendorRepository>(vendors);
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

        return new H(client, parts, audit, authorization);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Root(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static string Rv(H h, int id) => Convert.ToBase64String(h.Parts.Stored(id).RowVersion);
    private static List<string?> Errors(JsonElement root) => root.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();

    private static string CreateBody(string name = "V-Belt B42", int? machineId = 2, int? vendorId = 2, int? minimum = 4, int? current = 10) =>
        JsonSerializer.Serialize(new
        {
            sparePartName = name, category = "Belts", machineId, partNumber = "VB-42", unit = "Nos", minimumStock = minimum,
            currentStock = current, vendorId, storeLocation = "Rack B-3", unitCost = 350.75m,
        });

    private static string UpdateBody(string rowVersion, string name = "Hydraulic Seal Kit B", int? machineId = 1, int? vendorId = 1, int? current = 12) =>
        JsonSerializer.Serialize(new
        {
            sparePartName = name, category = "Hydraulics", machineId, partNumber = "HSK-100", unit = "Nos", minimumStock = 5,
            currentStock = current, vendorId, storeLocation = "Rack S-1", unitCost = 1250.50m, rowVersion,
        });

    // ================================================================ GET

    [Fact]
    public async Task GetAll_Returns200_WithThePagedEnvelope_AndStockValues()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync("/api/v1/spare-parts?pageNumber=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Spare parts retrieved successfully.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        var kit = data.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("sparePartCode").GetString() == "SPR-0001");
        Assert.Equal("Available", kit.GetProperty("stockStatus").GetString());
        Assert.Equal(12, kit.GetProperty("currentStock").GetInt32());
        Assert.Equal("MAC-0001", kit.GetProperty("machineCode").GetString());
        Assert.Equal("Chennai Hydraulics", kit.GetProperty("vendorName").GetString());
        Assert.Equal(1250.50m, kit.GetProperty("unitCost").GetDecimal());
        Assert.True(kit.GetProperty("isActive").GetBoolean());
        Assert.False(string.IsNullOrEmpty(kit.GetProperty("rowVersion").GetString()));
    }

    [Fact]
    public async Task GetAll_HonoursTheFilters_And404ForAnUnknownId()
    {
        var h = CreateHarness();
        async Task<int> Count(string qs) => (await Root(await h.Client.GetAsync("/api/v1/spare-parts" + qs))).GetProperty("data").GetProperty("totalCount").GetInt32();

        Assert.Equal(1, await Count("?search=heater"));
        Assert.Equal(1, await Count("?stockStatus=Low%20Stock"));
        Assert.Equal(1, await Count("?machineId=1"));
        Assert.Equal(1, await Count("?isActive=false"));

        var missing = await h.Client.GetAsync("/api/v1/spare-parts/999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.False((await Root(missing)).GetProperty("success").GetBoolean());
    }

    // ================================================================ POST

    [Fact]
    public async Task Post_Returns201_WithTheCodeAndComputedStatus_AndAudits()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/spare-parts", Json(CreateBody(current: 0)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("SPR-0004", data.GetProperty("sparePartCode").GetString());
        Assert.Equal(0, data.GetProperty("currentStock").GetInt32());
        Assert.Equal("Out of Stock", data.GetProperty("stockStatus").GetString());
        Assert.Equal(4, h.Parts.Count);
        Assert.Equal("SparePartCreated", Assert.Single(h.Audit.Entries).Action);
    }

    [Fact]
    public async Task Post_IgnoresSystemManagedFields()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/spare-parts", Json(CreateBody().Replace("\"sparePartName\"",
            "\"sparePartId\":99,\"sparePartCode\":\"HACK\",\"stockStatus\":\"Out of Stock\",\"isActive\":false,\"createdBy\":42,\"rowVersion\":\"AAAA\",\"sparePartName\"")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = h.Parts.Stored(4);
        Assert.Equal("SPR-0004", stored.SparePartCode);
        Assert.Equal("Available", stored.StockStatus);
        Assert.True(stored.IsActive);
        Assert.Equal(1, stored.CreatedBy);
    }

    [Fact]
    public async Task Post_Returns400_404_409_AsAppropriate_WritingNothing()
    {
        var h = CreateHarness();

        var invalid = await h.Client.PostAsync("/api/v1/spare-parts", Json("""{"sparePartName":" ","unit":"","currentStock":-1,"unitCost":-1}"""));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = Errors(await Root(invalid));
        foreach (var expected in new[] { "SparePartName is required.", "Unit is required.", "MinimumStock is required.", "CurrentStock cannot be negative.", "UnitCost cannot be negative." })
        {
            Assert.Contains(expected, errors);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PostAsync("/api/v1/spare-parts", Json(CreateBody(machineId: 999)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PostAsync("/api/v1/spare-parts", Json(CreateBody(vendorId: 999)))).StatusCode);
        var inactiveMachine = await h.Client.PostAsync("/api/v1/spare-parts", Json(CreateBody(machineId: 3)));
        Assert.Equal(HttpStatusCode.BadRequest, inactiveMachine.StatusCode);
        Assert.Contains("The selected machine is not active.", Errors(await Root(inactiveMachine)));
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PostAsync("/api/v1/spare-parts", Json(CreateBody(vendorId: 3)))).StatusCode);

        Assert.Equal(HttpStatusCode.Conflict, (await h.Client.PostAsync("/api/v1/spare-parts", Json(CreateBody(name: "HEATER BAND")))).StatusCode);

        Assert.Equal(3, h.Parts.Count);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ PUT

    [Fact]
    public async Task Put_Returns200_SavesCurrentStock_AndReturnsAFreshRowVersion()
    {
        var h = CreateHarness();
        var oldVersion = Rv(h, 1);

        var response = await h.Client.PutAsync("/api/v1/spare-parts/1", Json(UpdateBody(oldVersion, current: 5)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("Hydraulic Seal Kit B", data.GetProperty("sparePartName").GetString());
        Assert.Equal(5, data.GetProperty("currentStock").GetInt32());
        Assert.Equal("Low Stock", data.GetProperty("stockStatus").GetString());
        Assert.Equal("SPR-0001", data.GetProperty("sparePartCode").GetString());
        Assert.NotEqual(oldVersion, data.GetProperty("rowVersion").GetString());
        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal("SparePartUpdated", entry.Action);
        Assert.Contains(entry.Details, d => d is { FieldName: "current_stock", OldValue: "12", NewValue: "5" });
    }

    [Fact]
    public async Task Put_Returns409_ForAStaleRowVersion_WithTheCleanMessage()
    {
        var h = CreateHarness();
        var stale = Rv(h, 1);
        h.Parts.SimulateConcurrentModification(1);

        var response = await h.Client.PutAsync("/api/v1/spare-parts/1", Json(UpdateBody(stale)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("The spare part was modified by another user. Refresh the spare part and try again.", text);
        Assert.DoesNotContain("DbUpdateConcurrencyException", text);
        Assert.Equal("Hydraulic Seal Kit", h.Parts.Stored(1).SparePartName);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns400_404_409_AsAppropriate()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsync("/api/v1/spare-parts/1", Json(UpdateBody("")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsync("/api/v1/spare-parts/1", Json(UpdateBody(Rv(h, 1), current: -1)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PutAsync("/api/v1/spare-parts/999", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsync("/api/v1/spare-parts/1", Json(UpdateBody(Rv(h, 1), machineId: 3)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await h.Client.PutAsync("/api/v1/spare-parts/1", Json(UpdateBody(Rv(h, 1), name: "heater band")))).StatusCode);

        Assert.Equal("Hydraulic Seal Kit", h.Parts.Stored(1).SparePartName);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ DELETE (= soft deactivate)

    [Fact]
    public async Task Delete_Returns200_DeactivatesTheSparePart_KeepsTheRow_AndAudits_Then409()
    {
        var h = CreateHarness();

        var response = await h.Client.DeleteAsync("/api/v1/spare-parts/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Spare part deactivated successfully.", root.GetProperty("message").GetString());
        Assert.False(root.GetProperty("data").GetProperty("isActive").GetBoolean());
        Assert.False(h.Parts.Stored(1).IsActive);
        Assert.Equal(12, h.Parts.Stored(1).CurrentStock);
        Assert.Equal(3, h.Parts.Count);
        Assert.Equal("SparePartDeactivated", Assert.Single(h.Audit.Entries).Action);

        var again = await h.Client.DeleteAsync("/api/v1/spare-parts/1");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already inactive", await again.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.DeleteAsync("/api/v1/spare-parts/999")).StatusCode);
        Assert.Single(h.Audit.Entries);
    }

    // ================================================================ authorization

    [Fact]
    public async Task SpareParts_Endpoints_Return401_WithoutAToken_AndChangeNothing()
    {
        var h = CreateHarness(authenticate: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/spare-parts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/spare-parts/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PostAsync("/api/v1/spare-parts", Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PutAsync("/api/v1/spare-parts/1", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.DeleteAsync("/api/v1/spare-parts/1")).StatusCode);
        Assert.Equal(3, h.Parts.Count);
        Assert.True(h.Parts.Stored(1).IsActive);
        Assert.Empty(h.Authorization.Checks);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task SpareParts_Endpoints_Return403_WithoutThePermission_AndChangeNothing()
    {
        var h = CreateHarness(permissionGranted: false);

        var post = await h.Client.PostAsync("/api/v1/spare-parts", Json(CreateBody()));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/spare-parts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/spare-parts/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync("/api/v1/spare-parts/1", Json(UpdateBody(Rv(h, 1))))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync("/api/v1/spare-parts/1")).StatusCode);

        Assert.Equal("You do not have permission to perform this action.", (await Root(post)).GetProperty("message").GetString());
        Assert.Equal(3, h.Parts.Count);
        Assert.Equal("Hydraulic Seal Kit", h.Parts.Stored(1).SparePartName);
        Assert.True(h.Parts.Stored(1).IsActive);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task EachEndpoint_AsksForTheMasterSparePartPermission_MatchingItsVerb()
    {
        var h = CreateHarness();

        await h.Client.GetAsync("/api/v1/spare-parts");
        await h.Client.GetAsync("/api/v1/spare-parts/1");
        await h.Client.PostAsync("/api/v1/spare-parts", Json(CreateBody()));
        await h.Client.PutAsync("/api/v1/spare-parts/1", Json(UpdateBody(Rv(h, 1))));
        await h.Client.DeleteAsync("/api/v1/spare-parts/1");

        Assert.Equal(
            new[] { PermissionAction.View, PermissionAction.View, PermissionAction.Add, PermissionAction.Edit, PermissionAction.Delete },
            h.Authorization.Checks.Select(c => c.Action));
        Assert.All(h.Authorization.Checks, c =>
        {
            Assert.Equal(ModuleCodes.MasterSparePart, c.ModuleCode);
            Assert.Equal("ADMIN", c.RoleCode);
        });
    }

    [Fact]
    public async Task ViewOnly_CanReadButNotWrite_DecidedByTheMatrixNotTheRoleName()
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.MasterSparePart && action == PermissionAction.View);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync("/api/v1/spare-parts")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync("/api/v1/spare-parts/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PostAsync("/api/v1/spare-parts", Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync("/api/v1/spare-parts/1", Json(UpdateBody(Rv(h, 1))))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync("/api/v1/spare-parts/1")).StatusCode);
        Assert.Equal(3, h.Parts.Count);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task MachineOrVendorPermissions_DoNotGrantSparePartAccess()
    {
        var h = CreateHarness(decide: (_, module, _) => module is ModuleCodes.MasterMachine or ModuleCodes.MasterVendor);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/spare-parts")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PostAsync("/api/v1/spare-parts", Json(CreateBody()))).StatusCode);
        Assert.Equal(3, h.Parts.Count);
    }
}
