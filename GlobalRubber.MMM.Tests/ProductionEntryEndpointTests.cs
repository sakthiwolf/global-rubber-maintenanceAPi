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
/// /api/v1/production-entries through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model
/// binding) with the REAL ProductionEntryService. Only persistence is faked (ProductionEntryTestData). Acting user 1.
/// </summary>
public class ProductionEntryEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string Url = "/api/v1/production-entries";
    private readonly ApiWebApplicationFactory _factory;

    public ProductionEntryEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemoryProductionEntryRepository Entries, RecordingAuditLog Audit, StubPermissionAuthorization Authorization);

    private H CreateHarness(bool permissionGranted = true, bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machineList = MachineTestData.Machines();
        var productList = ProductTestData.Products();
        var moldList = ProductionEntryTestData.Molds();
        var products = new InMemoryProductRepository(productList);
        var entries = new InMemoryProductionEntryRepository(moldList, machineList, productList);
        var audit = new RecordingAuditLog();
        var authorization = decide is null ? new StubPermissionAuthorization(permissionGranted) : new StubPermissionAuthorization(decide);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IProductionEntryRepository>();
                services.AddSingleton<IProductionEntryRepository>(entries);
                services.RemoveAll<IMachineRepository>();
                services.AddSingleton<IMachineRepository>(new InMemoryMachineRepository(machineList, departments, employees));
                services.RemoveAll<IProductRepository>();
                services.AddSingleton<IProductRepository>(products);
                services.RemoveAll<IMoldPmRepository>();
                services.AddSingleton<IMoldPmRepository>(new InMemoryMoldPmRepository(moldList));
                services.RemoveAll<INotificationRepository>();
                services.AddSingleton<INotificationRepository>(new InMemoryNotificationRepository());
                services.RemoveAll<IMoldRepository>();
                services.AddSingleton<IMoldRepository>(new InMemoryMoldRepository(moldList, products, employees));
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

        return new H(client, entries, audit, authorization);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Root(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static string Body(int mold = 1, int product = 1, int machine = 1, int qty = 1000, int rejected = 0, string shift = "Shift A") =>
        JsonSerializer.Serialize(new { entryDate = "2026-09-25", shift, machineId = machine, productId = product, moldId = mold, productionQty = qty, rejectedQty = rejected, remarks = "ok" });

    // ================================================================ POST

    [Fact]
    public async Task Post_Returns201_WithTheNumber_UsageSnapshot_AndALocationHeader_AndAudits()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync(Url, Json(Body(qty: 1200, rejected: 200)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var root = await Root(response);
        Assert.Equal("Production entry saved successfully.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal("PROD-0001", data.GetProperty("entryNo").GetString());
        Assert.Equal("2026-09-25", data.GetProperty("entryDate").GetString());
        Assert.Equal(1000, data.GetProperty("goodQty").GetInt32());
        Assert.Equal(100_000, data.GetProperty("moldUsageBefore").GetInt32());
        Assert.Equal(101_200, data.GetProperty("moldUsageAfter").GetInt32());
        Assert.Equal("Saved", data.GetProperty("status").GetString());
        Assert.Equal("MAC-0001", data.GetProperty("machineCode").GetString());
        Assert.Equal(101_200, h.Entries.StoredMold(1).CurrentUsageShots);
        Assert.Equal("ProductionEntryCreated", Assert.Single(h.Audit.Entries).Action);
    }

    [Fact]
    public async Task Post_IgnoresClientSuppliedServerControlledFields()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync(Url, Json(
            """{"entryDate":"2026-09-25","shift":"Shift A","machineId":1,"productId":1,"moldId":1,"productionQty":10,"rejectedQty":2,"productionEntryId":99,"entryNo":"HACK-1","goodQty":999,"moldUsageBefore":1,"moldUsageAfter":2,"status":"Cancelled","createdBy":42,"rowVersion":"AAAA"}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = h.Entries.Stored(1);
        Assert.Equal("PROD-0001", stored.EntryNo);
        Assert.Equal(8, stored.GoodQty);
        Assert.Equal((100_000, 100_010), (stored.MoldUsageBefore, stored.MoldUsageAfter));
        Assert.Equal("Saved", stored.Status);
        Assert.Equal(1, stored.CreatedBy);
    }

    [Fact]
    public async Task Post_Returns400_ForInvalidFields_WithEveryMessage_WritingNothing()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync(Url, Json("""{"shift":"Night","productionQty":5,"rejectedQty":6}"""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await Root(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("EntryDate is required.", errors);
        Assert.Contains("Shift must be one of: Shift A, Shift B, Shift C.", errors);
        Assert.Contains("MachineId is required.", errors);
        Assert.Contains("Rejected quantity cannot be more than production quantity.", errors);
        Assert.Equal(0, h.Entries.Count);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Post_Returns400_404_409_ForTheReferenceAndLifeRules_WritingNothing()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PostAsync(Url, Json(Body(machine: 3)))).StatusCode);       // inactive machine
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PostAsync(Url, Json(Body(mold: 5, product: 3)))).StatusCode); // inactive product
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PostAsync(Url, Json(Body(mold: 3, product: 1)))).StatusCode); // mold of another product
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PostAsync(Url, Json(Body(machine: 999)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PostAsync(Url, Json(Body(mold: 999)))).StatusCode);

        var blocked = await h.Client.PostAsync(Url, Json(Body(mold: 3, product: 2, qty: 1)));
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var text = await blocked.Content.ReadAsStringAsync();
        Assert.Contains("This mold has reached its replacement limit and cannot be used for production. Please select another mold.", text);
        Assert.DoesNotContain("Exception", text);

        Assert.Equal(0, h.Entries.Count);
        Assert.Equal(0, h.Entries.LastNumber);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task ThereIsNoUpdateCancelOrDeleteEndpoint_Q14()
    {
        var h = CreateHarness();
        await h.Client.PostAsync(Url, Json(Body()));

        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await h.Client.PutAsync($"{Url}/1", Json(Body()))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await h.Client.DeleteAsync($"{Url}/1")).StatusCode);
        Assert.Equal("Saved", h.Entries.Stored(1).Status);
    }

    // ================================================================ GET

    [Fact]
    public async Task GetAll_Returns200_NewestFirst_TenPerPageByDefault_AndHonoursTheFilters()
    {
        var h = CreateHarness();
        for (var i = 0; i < 12; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await h.Client.PostAsync(Url, Json(Body(qty: 1, machine: i < 4 ? 2 : 1)))).StatusCode);
        }

        var root = await Root(await h.Client.GetAsync(Url));
        Assert.Equal("Production entries retrieved successfully.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal((10, 10, 12, 2), (data.GetProperty("pageSize").GetInt32(), data.GetProperty("items").GetArrayLength(), data.GetProperty("totalCount").GetInt32(), data.GetProperty("totalPages").GetInt32()));
        Assert.Equal("PROD-0012", data.GetProperty("items")[0].GetProperty("entryNo").GetString());

        var page2 = (await Root(await h.Client.GetAsync($"{Url}?pageNumber=2&pageSize=10"))).GetProperty("data");
        Assert.Equal(2, page2.GetProperty("items").GetArrayLength());
        Assert.Equal("PROD-0001", page2.GetProperty("items")[1].GetProperty("entryNo").GetString());

        async Task<int> Count(string qs) => (await Root(await h.Client.GetAsync(Url + qs))).GetProperty("data").GetProperty("totalCount").GetInt32();
        Assert.Equal(4, await Count("?machineId=2"));
        Assert.Equal(12, await Count("?moldId=1&fromDate=2026-09-25&toDate=2026-09-25"));
        Assert.Equal(0, await Count("?fromDate=2026-09-26"));
        Assert.Equal(1, await Count("?search=prod-0003"));
    }

    [Fact]
    public async Task GetById_Returns200_And404_ForUnknown()
    {
        var h = CreateHarness();
        await h.Client.PostAsync(Url, Json(Body()));

        var data = (await Root(await h.Client.GetAsync($"{Url}/1"))).GetProperty("data");
        Assert.Equal("PROD-0001", data.GetProperty("entryNo").GetString());
        Assert.Equal("Rubber Seal A", data.GetProperty("productName").GetString());

        var missing = await h.Client.GetAsync($"{Url}/999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.False((await Root(missing)).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task GetLookups_Returns200_ActiveMachinesAndProducts_AndMoldsWithLifeFigures()
    {
        var h = CreateHarness();

        var data = (await Root(await h.Client.GetAsync($"{Url}/lookups"))).GetProperty("data");

        Assert.Equal(2, data.GetProperty("machines").GetArrayLength());
        Assert.Equal(2, data.GetProperty("products").GetArrayLength());
        var molds = data.GetProperty("molds");
        Assert.Equal(4, molds.GetArrayLength());
        var first = molds[0];
        Assert.Equal(100_000, first.GetProperty("currentUsageShots").GetInt32());
        Assert.Equal(500_000, first.GetProperty("replacementShots").GetInt32());
        Assert.Equal("Normal", first.GetProperty("lifeState").GetString());
    }

    // ================================================================ authorization

    [Fact]
    public async Task Endpoints_Return401_WithoutAToken_AndChangeNothing()
    {
        var h = CreateHarness(authenticate: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync($"{Url}/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync($"{Url}/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PostAsync(Url, Json(Body()))).StatusCode);
        Assert.Equal(0, h.Entries.Count);
        Assert.Empty(h.Authorization.Checks);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Endpoints_Return403_WithoutThePermission_AndChangeNothing()
    {
        var h = CreateHarness(permissionGranted: false);

        var post = await h.Client.PostAsync(Url, Json(Body()));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync($"{Url}/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync($"{Url}/lookups")).StatusCode);

        Assert.Equal("You do not have permission to perform this action.", (await Root(post)).GetProperty("message").GetString());
        Assert.Equal(0, h.Entries.Count);
        Assert.Equal(100_000, h.Entries.StoredMold(1).CurrentUsageShots);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task EachEndpoint_AsksForTheTrnProductionEntryPermission_MatchingItsVerb()
    {
        var h = CreateHarness();

        await h.Client.GetAsync(Url);
        await h.Client.GetAsync($"{Url}/lookups");
        await h.Client.PostAsync(Url, Json(Body()));
        await h.Client.GetAsync($"{Url}/1");

        Assert.Equal(
            new[] { PermissionAction.View, PermissionAction.View, PermissionAction.Add, PermissionAction.View },
            h.Authorization.Checks.Select(c => c.Action));
        Assert.All(h.Authorization.Checks, c =>
        {
            Assert.Equal(ModuleCodes.TrnProductionEntry, c.ModuleCode);
            Assert.Equal("ADMIN", c.RoleCode);
        });
    }

    [Fact]
    public async Task ViewWithoutAdd_CanListButCannotSave_DecidedByTheMatrixNotTheRoleName()
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.TrnProductionEntry && action == PermissionAction.View);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync($"{Url}/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PostAsync(Url, Json(Body()))).StatusCode);
        Assert.Equal(0, h.Entries.Count);
    }

    [Fact]
    public async Task MasterPermissions_DoNotGrantProductionEntryAccess()
    {
        var h = CreateHarness(decide: (_, module, _) => module is ModuleCodes.MasterMold or ModuleCodes.MasterMachine or ModuleCodes.MasterProduct);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PostAsync(Url, Json(Body()))).StatusCode);
        Assert.Equal(0, h.Entries.Count);
    }
}
