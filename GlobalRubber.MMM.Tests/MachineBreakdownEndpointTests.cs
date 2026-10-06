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
/// /api/v1/machine-breakdowns through the real HTTP pipeline (JWT, [RequirePermission],
/// GlobalExceptionHandler, model binding) with the real MachineBreakdownService.
/// Only persistence is faked. Acting user 1.
/// </summary>
public class MachineBreakdownEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string Url = "/api/v1/machine-breakdowns";
    private readonly ApiWebApplicationFactory _factory;

    public MachineBreakdownEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(
        HttpClient Client,
        InMemoryMachineBreakdownRepository Breakdowns,
        RecordingAuditLog Audit,
        StubPermissionAuthorization Authorization);

    private H CreateHarness(bool permissionGranted = true, bool authenticate = true,
        Func<string, string, PermissionAction, bool>? decide = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machines = new InMemoryMachineRepository(MachineTestData.Machines(), departments, employees);
        var breakdownList = MachineBreakdownTestData.Breakdowns();
        var breakdowns = new InMemoryMachineBreakdownRepository(breakdownList, MachineTestData.Machines());
        var audit = new RecordingAuditLog();
        var authorization = decide is null
            ? new StubPermissionAuthorization(permissionGranted)
            : new StubPermissionAuthorization(decide);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMachineBreakdownRepository>();
                services.AddSingleton<IMachineBreakdownRepository>(breakdowns);
                services.RemoveAll<IMachineRepository>();
                services.AddSingleton<IMachineRepository>(machines);
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

        return new H(client, breakdowns, audit, authorization);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Root(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static string CreateBody(
        int machineId = 1, string date = "2026-09-25", string time = "09:30:00",
        string? reportedBy = "John Doe", string problem = "Spindle vibration",
        string priority = "Medium") =>
        JsonSerializer.Serialize(new
        {
            machineId, breakdownDate = date, breakdownTime = time,
            reportedBy, problem, priority,
        });

    // ========================================================= POST

    [Fact]
    public async Task Post_Returns201_WithBreakdownNo_AndAudits()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync(Url, Json(CreateBody()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var root = await Root(response);
        Assert.Equal("Breakdown reported successfully.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal("BRK-0003", data.GetProperty("breakdownNo").GetString());
        Assert.Equal("Reported", data.GetProperty("stage").GetString());
        Assert.Equal("John Doe", data.GetProperty("reportedBy").GetString());
        Assert.Equal(BreakdownAuditNames.Created, Assert.Single(h.Audit.Entries).Action);
    }

    [Fact]
    public async Task Post_ReportedBy_StoredAsFreetextNotEmployee()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync(Url, Json(CreateBody(reportedBy: "Freelancer External")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("Freelancer External", data.GetProperty("reportedBy").GetString());
    }

    [Fact]
    public async Task Post_AcceptsTimeWithoutSeconds_HHmm()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync(Url, Json(CreateBody(time: "18:54")));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        // The request may omit seconds; the response always uses the API's time format HH:mm:ss (TimeOnly converter,
        // and the frontend model BackendMachineBreakdown.breakdownTime documents HH:mm:ss).
        Assert.Equal("18:54:00", data.GetProperty("breakdownTime").GetString());
    }

    [Fact]
    public async Task Post_Returns400_ForMissingRequiredFields()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync(Url, Json("{}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await Root(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("MachineId is required.", errors);
        Assert.Contains("BreakdownDate is required.", errors);
        Assert.Contains("BreakdownTime is required.", errors);
        Assert.Contains("Problem is required.", errors);
        // Nothing persisted.
        Assert.Equal(2, h.Breakdowns.Count); // only the seeded breakdowns
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Post_Returns400_ForInactiveMachine()
    {
        var h = CreateHarness();

        // Machine 3 is inactive in MachineTestData.
        var response = await h.Client.PostAsync(Url, Json(CreateBody(machineId: 3)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(2, h.Breakdowns.Count);
    }

    [Fact]
    public async Task Post_Returns404_ForUnknownMachine()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync(Url, Json(CreateBody(machineId: 9999)));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns401_WhenUnauthenticated()
    {
        var h = CreateHarness(authenticate: false);

        var response = await h.Client.PostAsync(Url, Json(CreateBody()));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_Returns403_WhenPermissionDenied()
    {
        var h = CreateHarness(permissionGranted: false);

        var response = await h.Client.PostAsync(Url, Json(CreateBody()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(2, h.Breakdowns.Count);
    }

    [Fact]
    public async Task Post_ChecksAddPermission()
    {
        var h = CreateHarness(decide: (role, module, action) => true);

        await h.Client.PostAsync(Url, Json(CreateBody()));

        Assert.Contains(h.Authorization.Checks, c => c.ModuleCode == ModuleCodes.TrnMachineBreakdown && c.Action == PermissionAction.Add);
    }

    // ========================================================= GET list

    [Fact]
    public async Task GetAll_Returns200_WithPagedResult()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync(Url + "?pageNumber=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal(2, data.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task GetAll_ChecksViewPermission()
    {
        var h = CreateHarness(decide: (role, module, action) => true);

        await h.Client.GetAsync(Url);

        Assert.Contains(h.Authorization.Checks, c => c.ModuleCode == ModuleCodes.TrnMachineBreakdown && c.Action == PermissionAction.View);
    }

    // ========================================================= GET by id

    [Fact]
    public async Task GetById_Returns200_WithBreakdown()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync(Url + "/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("BRK-0001", data.GetProperty("breakdownNo").GetString());
    }

    [Fact]
    public async Task GetById_Returns404_ForUnknownId()
    {
        var response = await CreateHarness().Client.GetAsync(Url + "/9999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ========================================================= PUT advance-stage

    [Fact]
    public async Task AdvanceStage_Returns200_WhenValid()
    {
        var h = CreateHarness();

        // Get the current rowVersion for breakdown 1 (Reported).
        var getResp = await h.Client.GetAsync(Url + "/1");
        var rv = (await Root(getResp)).GetProperty("data").GetProperty("rowVersion").GetString()!;

        var body = JsonSerializer.Serialize(new { stage = "Assigned", rowVersion = rv });
        var response = await h.Client.PutAsync(Url + "/1/advance-stage", Json(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("Assigned", data.GetProperty("stage").GetString());
        Assert.Equal(BreakdownAuditNames.StageChanged, Assert.Single(h.Audit.Entries).Action);
    }

    [Fact]
    public async Task AdvanceStage_Returns409_ForStaleRowVersion()
    {
        var h = CreateHarness();

        var stale = Convert.ToBase64String(new byte[] { 0xFF, 0xFF });
        var body = JsonSerializer.Serialize(new { stage = "Assigned", rowVersion = stale });
        var response = await h.Client.PutAsync(Url + "/1/advance-stage", Json(body));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task AdvanceStage_Returns400_ForSkippingStage()
    {
        var h = CreateHarness();

        var getResp = await h.Client.GetAsync(Url + "/1");
        var rv = (await Root(getResp)).GetProperty("data").GetProperty("rowVersion").GetString()!;

        var body = JsonSerializer.Serialize(new { stage = "Maintenance Started", rowVersion = rv });
        var response = await h.Client.PutAsync(Url + "/1/advance-stage", Json(body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<HttpResponseMessage> AdvanceAsync(H h, int id, object extra)
    {
        var rv = (await Root(await h.Client.GetAsync($"{Url}/{id}"))).GetProperty("data").GetProperty("rowVersion").GetString()!;
        var body = JsonSerializer.Serialize(extra).TrimEnd('}') + $",\"rowVersion\":\"{rv}\"}}";
        return await h.Client.PutAsync($"{Url}/{id}/advance-stage", Json(body));
    }

    [Fact]
    public async Task AdvanceStage_AssignsAnEngineer_OrRefusesAnUnknown404_OrInactive400()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.NotFound, (await AdvanceAsync(h, 1, new { stage = "Assigned", assignedEngineerId = 999 })).StatusCode);
        var inactive = await AdvanceAsync(h, 1, new { stage = "Assigned", assignedEngineerId = 3 });
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Contains("The selected engineer is not active.", await inactive.Content.ReadAsStringAsync());

        var ok = await AdvanceAsync(h, 1, new { stage = "Assigned", assignedEngineerId = 1 });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var data = (await Root(ok)).GetProperty("data");
        Assert.Equal((1, "Ravi Kumar"), (data.GetProperty("assignedEngineerId").GetInt32(), data.GetProperty("assignedEngineerName").GetString()));
    }

    [Fact]
    public async Task AdvanceStage_RootCauseOver1000_Is400_NotA500()
    {
        var h = CreateHarness();
        Assert.Equal(HttpStatusCode.OK, (await AdvanceAsync(h, 2, new { stage = "Maintenance Started" })).StatusCode);

        var response = await AdvanceAsync(h, 2, new { stage = "Resolved", rootCause = new string('r', 1001), correctiveAction = new string('c', 1001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("RootCause must be at most 1000 characters.", text);
        Assert.Contains("CorrectiveAction must be at most 1000 characters.", text);
    }

    [Fact]
    public async Task AdvanceStage_OnAClosedBreakdown_Is409()
    {
        var h = CreateHarness();
        foreach (var stage in new[] { "Maintenance Started", "Resolved", "Closed" })
        {
            Assert.Equal(HttpStatusCode.OK, (await AdvanceAsync(h, 2, new { stage })).StatusCode);
        }

        var response = await AdvanceAsync(h, 2, new { stage = "Closed" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("already closed", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("?pageNumber=0&pageSize=10")]
    [InlineData("?pageNumber=-5&pageSize=10")]
    [InlineData("?pageNumber=1&pageSize=0")]
    [InlineData("?pageNumber=1&pageSize=101")]
    public async Task GetAll_InvalidPaging_Is400(string queryString)
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync(Url + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task CloseBreakdown2(H h)
    {
        foreach (var stage in new[] { "Maintenance Started", "Resolved", "Closed" })
        {
            Assert.Equal(HttpStatusCode.OK, (await AdvanceAsync(h, 2, new { stage })).StatusCode);
        }
    }

    private async Task<HttpResponseMessage> ReopenAsync(H h, int id)
    {
        var rv = (await Root(await h.Client.GetAsync($"{Url}/{id}"))).GetProperty("data").GetProperty("rowVersion").GetString()!;
        return await h.Client.PutAsync($"{Url}/{id}/reopen", Json(JsonSerializer.Serialize(new { rowVersion = rv })));
    }

    [Fact]
    public async Task Reopen_RequiresApprove_EditAloneIs403()
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.TrnMachineBreakdown && action != PermissionAction.Approve);
        await CloseBreakdown2(h);

        Assert.Equal(HttpStatusCode.Forbidden, (await ReopenAsync(h, 2)).StatusCode);
        Assert.Equal("Closed", h.Breakdowns.Stored(2).Stage);
        Assert.Contains(h.Authorization.Checks, c => c.ModuleCode == ModuleCodes.TrnMachineBreakdown && c.Action == PermissionAction.Approve);
    }

    [Fact]
    public async Task Reopen_WithApprove_Returns200_BackToResolved_AndAudits()
    {
        var h = CreateHarness();
        await CloseBreakdown2(h);

        var response = await ReopenAsync(h, 2);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal(("Resolved", "BRK-0002"), (data.GetProperty("stage").GetString(), data.GetProperty("breakdownNo").GetString()));
        Assert.Equal(JsonValueKind.Null, data.GetProperty("closedAt").ValueKind);
        Assert.Equal(BreakdownAuditNames.Reopened, h.Audit.Entries.Last().Action);
        Assert.Equal(HttpStatusCode.Conflict, (await ReopenAsync(h, 2)).StatusCode); // no longer Closed
    }

    [Fact]
    public async Task Assign_ByTypedName_IsStoredAsText()
    {
        var h = CreateHarness();

        var ok = await AdvanceAsync(h, 1, new { stage = "Assigned", assignedToName = "Outside Contractor" });

        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var data = (await Root(ok)).GetProperty("data");
        Assert.Equal("Outside Contractor", data.GetProperty("assignedToName").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("assignedEngineerId").ValueKind);
    }

    [Fact]
    public async Task GetAll_ActiveOnly_LeavesOutClosedBreakdowns()
    {
        var h = CreateHarness();
        await CloseBreakdown2(h);

        var items = (await Root(await h.Client.GetAsync($"{Url}?activeOnly=true"))).GetProperty("data").GetProperty("items");

        Assert.Equal(new[] { "BRK-0001" }, items.EnumerateArray().Select(i => i.GetProperty("breakdownNo").GetString()));
    }

    [Fact]
    public async Task AdvanceStage_ChecksEditPermission()
    {
        var h = CreateHarness(decide: (role, module, action) => true);

        var getResp = await h.Client.GetAsync(Url + "/1");
        var rv = (await Root(getResp)).GetProperty("data").GetProperty("rowVersion").GetString()!;
        var body = JsonSerializer.Serialize(new { stage = "Assigned", rowVersion = rv });
        await h.Client.PutAsync(Url + "/1/advance-stage", Json(body));

        Assert.Contains(h.Authorization.Checks,
            c => c.ModuleCode == ModuleCodes.TrnMachineBreakdown && c.Action == PermissionAction.Edit);
    }
}
