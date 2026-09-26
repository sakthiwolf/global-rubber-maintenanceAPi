using System.Net;
using System.Text;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlobalRubber.MMM.Tests;

/// <summary>HTTP contract of /api/v1/spare-part-usage through the real pipeline (JWT, RequirePermission, ApiResponse). No database.</summary>
public class SparePartUsageEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public SparePartUsageEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemorySparePartUsageRepository Usages, List<SparePart> Parts, StubPermissionAuthorization Authorization);

    private H CreateHarness(Func<string, string, PermissionAction, bool>? decide = null, bool authenticate = true)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machines = new InMemoryMachineRepository(MachineTestData.Machines(), departments, employees);
        var vendors = new InMemoryVendorRepository(VendorTestData.Vendors());
        var partList = SparePartUsageTestData.Parts();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(330)); // the real DateTimeProvider's IST date
        var usages = new InMemorySparePartUsageRepository(partList, today);
        var authorization = new StubPermissionAuthorization(decide ?? ((_, _, _) => true));

        var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ISparePartUsageRepository>();
            services.AddSingleton<ISparePartUsageRepository>(usages);
            services.RemoveAll<ISparePartRepository>();
            services.AddSingleton<ISparePartRepository>(new InMemorySparePartRepository(partList, machines, vendors));
            services.RemoveAll<IEmployeeRepository>();
            services.AddSingleton<IEmployeeRepository>(employees);
            services.RemoveAll<INotificationRepository>();
            services.AddSingleton<INotificationRepository>(new InMemoryNotificationRepository());
            services.RemoveAll<IUserRepository>();
            services.AddSingleton<IUserRepository>(users);
            services.RemoveAll<IAuditLogService>();
            services.AddSingleton<IAuditLogService>(new RecordingAuditLog());
            services.RemoveAll<IPermissionAuthorizationService>();
            services.AddSingleton<IPermissionAuthorizationService>(authorization);
        }));

        var client = factory.CreateClient();
        if (authenticate) TestAuth.Authenticate(client, factory, "ADMIN", 1);
        return new H(client, usages, partList, authorization);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Root(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static string Body(int partId, decimal qty, string? requestId = null, string type = "Machine PM", int pmId = 20) =>
        $"{{\"maintenanceType\":\"{type}\",\"maintenanceId\":{pmId},\"sparePartId\":{partId},\"quantity\":{qty.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
        $"\"usageDate\":\"{DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(330)):yyyy-MM-dd}\"{(requestId is null ? string.Empty : $",\"requestId\":\"{requestId}\"")}}}";

    [Fact]
    public async Task Post_201_ThenTheSameRequestId_200_WithTheSameUsage_StockDeductedOnce()
    {
        var h = CreateHarness();
        var id = Guid.NewGuid().ToString();

        var first = await h.Client.PostAsync("/api/v1/spare-part-usage", Json(Body(10, 3, id)));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var data = (await Root(first)).GetProperty("data");
        Assert.Equal(("SPU-0001", "Nos", "MPM-0010"), (data.GetProperty("usageNo").GetString(), data.GetProperty("unit").GetString(), data.GetProperty("maintenanceNo").GetString()));

        var replay = await h.Client.PostAsync("/api/v1/spare-part-usage", Json(Body(10, 3, id)));
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("SPU-0001", (await Root(replay)).GetProperty("data").GetProperty("usageNo").GetString());
        Assert.Equal(7, h.Parts.Single(p => p.SparePartId == 10).CurrentStock);
    }

    [Fact]
    public async Task InsufficientStock_409_WithTheMessage()
    {
        var h = CreateHarness();
        var r = await h.Client.PostAsync("/api/v1/spare-part-usage", Json(Body(11, 6)));

        Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
        Assert.StartsWith("Insufficient stock available.", (await Root(r)).GetProperty("message").GetString());
        Assert.Equal(5, h.Parts.Single(p => p.SparePartId == 11).CurrentStock);
    }

    [Fact]
    public async Task Validation_400_UnknownMaintenance_404()
    {
        var h = CreateHarness();
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PostAsync("/api/v1/spare-part-usage", Json(Body(10, 1.5m)))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PostAsync("/api/v1/spare-part-usage", Json(Body(10, 0)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PostAsync("/api/v1/spare-part-usage", Json(Body(10, 1, pmId: 999)))).StatusCode);
        Assert.Empty(h.Usages.Usages);
    }

    [Fact]
    public async Task Permissions_ViewOnly_CannotPostOrReverse_NoView_CannotRead_NoToken_401()
    {
        var viewOnly = CreateHarness(decide: (_, module, action) => module == ModuleCodes.TrnSparePartUsage && action == PermissionAction.View);
        Assert.Equal(HttpStatusCode.OK, (await viewOnly.Client.GetAsync("/api/v1/spare-part-usage")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewOnly.Client.GetAsync("/api/v1/spare-part-usage/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.Client.PostAsync("/api/v1/spare-part-usage", Json(Body(10, 1)))).StatusCode);
        var del = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/spare-part-usage/1") { Content = Json("{\"reason\":\"x\",\"rowVersion\":\"AQ==\"}") };
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.Client.SendAsync(del)).StatusCode);
        Assert.Empty(viewOnly.Usages.Usages);
        Assert.Equal(10, viewOnly.Parts.Single(p => p.SparePartId == 10).CurrentStock);

        var none = CreateHarness(decide: (_, _, _) => false);
        Assert.Equal(HttpStatusCode.Forbidden, (await none.Client.GetAsync("/api/v1/spare-part-usage")).StatusCode);

        var anon = CreateHarness(authenticate: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.Client.GetAsync("/api/v1/spare-part-usage")).StatusCode);
    }

    [Fact]
    public async Task PostNeedsAdd_ReverseNeedsDelete()
    {
        var h = CreateHarness();
        await h.Client.PostAsync("/api/v1/spare-part-usage", Json(Body(10, 3)));
        var rv = Convert.ToBase64String(h.Usages.Usages[0].RowVersion);

        var del = new HttpRequestMessage(HttpMethod.Delete, "/api/v1/spare-part-usage/1") { Content = Json($"{{\"reason\":\"Wrong part\",\"rowVersion\":\"{rv}\"}}") };
        var r = await h.Client.SendAsync(del);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("Reversed", (await Root(r)).GetProperty("data").GetProperty("status").GetString());
        Assert.Equal(10, h.Parts.Single(p => p.SparePartId == 10).CurrentStock);

        Assert.Contains(h.Authorization.Checks, c => c.ModuleCode == ModuleCodes.TrnSparePartUsage && c.Action == PermissionAction.Add);
        Assert.Contains(h.Authorization.Checks, c => c.ModuleCode == ModuleCodes.TrnSparePartUsage && c.Action == PermissionAction.Delete);
    }

    [Fact]
    public async Task NoStockShortcut_EditOrAdjustIsNotAnEndpoint()
    {
        var h = CreateHarness();
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await h.Client.PutAsync("/api/v1/spare-part-usage/1", Json("{\"quantity\":1}"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PostAsync("/api/v1/spare-parts/10/reduce-stock", Json("{\"quantity\":1}"))).StatusCode);
        Assert.Equal(10, h.Parts.Single(p => p.SparePartId == 10).CurrentStock);
    }
}
