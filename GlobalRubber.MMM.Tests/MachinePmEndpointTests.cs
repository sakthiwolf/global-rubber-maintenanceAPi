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

/// <summary>
/// /api/v1/machine-maintenance through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model
/// binding) with the REAL MachinePmService. The real clock stays in place (the JWT handler uses it too), so dates are
/// relative to the plant's real "today". Only persistence is faked (MachinePmScenario / InMemoryMachinePmRepository).
/// Occurrences come from the checklist and from completion - there is no scheduling endpoint.
/// </summary>
public class MachinePmEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string Url = "/api/v1/machine-maintenance";
    private static readonly DateOnly Today = new GlobalRubber.MMM.Infrastructure.Services.DateTimeProvider().Today;
    private static string Day(int offset = 0) => Today.AddDays(offset).ToString("yyyy-MM-dd");
    private readonly ApiWebApplicationFactory _factory;

    public MachinePmEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, MachinePmScenario Data, InMemoryMachinePmRepository Pms, RecordingAuditLog Audit, StubPermissionAuthorization Authorization);

    private H CreateHarness(MachinePmScenario? data = null, bool permissionGranted = true, bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        data ??= new MachinePmScenario();
        var pms = new InMemoryMachinePmRepository(data);
        var audit = new RecordingAuditLog();
        var authorization = decide is null ? new StubPermissionAuthorization(permissionGranted) : new StubPermissionAuthorization(decide);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMachinePmRepository>();
                services.AddSingleton<IMachinePmRepository>(pms);
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

        return new H(client, data, pms, audit, authorization);
    }

    /// <summary>One Daily checklist (anchored today) on MAC-0001 with its open occurrence MPM-0001 due today.</summary>
    private static (MachinePmScenario Data, MaintenanceChecklist Checklist, MachinePm Pm) OneOpen()
    {
        var data = new MachinePmScenario();
        var checklist = data.Checklist("Daily", Today);
        return (data, checklist, data.Open(checklist, Today));
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Root(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static string Complete(MachinePm pm, string? by = "Ravi", string? rowVersion = null, object? results = null) =>
        JsonSerializer.Serialize(new { maintenanceBy = by, rowVersion = rowVersion ?? Convert.ToBase64String(pm.RowVersion), remarks = "done", results = results ?? Array.Empty<object>() });

    // ================================================================ complete

    [Fact]
    public async Task PutComplete_Returns200_Completes_CreatesTheSuccessor_AndUpdatesTheMachine()
    {
        var (data, checklist, pm) = OneOpen();
        var h = CreateHarness(data);
        var line = pm.ChecklistItems[0].MachinePmChecklistId;

        var response = await h.Client.PutAsync($"{Url}/{pm.MachinePmId}/complete", Json(Complete(pm, by: "  Ravi Kumar ", results: new[] { new { machinePmChecklistId = line, isChecked = true } })));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Maintenance completed successfully.", root.GetProperty("message").GetString());
        var dto = root.GetProperty("data");
        Assert.Equal(("Completed", Day(), "Ravi Kumar", Day()), (dto.GetProperty("status").GetString(), dto.GetProperty("completedDate").GetString(), dto.GetProperty("maintenanceBy").GetString(), dto.GetProperty("scheduledDate").GetString()));
        Assert.Equal(("Daily", checklist.ChecklistName), (dto.GetProperty("frequency").GetString(), dto.GetProperty("checklistName").GetString()));
        Assert.True(dto.GetProperty("checklistItems")[0].GetProperty("isChecked").GetBoolean());
        Assert.False(dto.GetProperty("checklistItems")[1].GetProperty("isChecked").GetBoolean());
        Assert.False(dto.TryGetProperty("engineerId", out _));

        var successor = h.Pms.StoredByNo("MPM-0002");
        Assert.Equal((Today.AddDays(1), "Scheduled", 1), (successor.ScheduledDate, successor.Status, successor.MachineId));
        Assert.Equal((Today, Today.AddDays(1)), (h.Pms.StoredMachine(1).LastMaintenanceDate!.Value, h.Pms.StoredMachine(1).NextMaintenanceDate!.Value));
        Assert.Equal(new[] { "MachinePmCompleted", "MachinePmScheduled" }, h.Audit.Entries.Select(e => e.Action));
    }

    [Fact]
    public async Task PutComplete_IgnoresServerControlledFields()
    {
        var (data, _, pm) = OneOpen();
        var h = CreateHarness(data);

        var response = await h.Client.PutAsync($"{Url}/{pm.MachinePmId}/complete", Json(JsonSerializer.Serialize(new
        {
            maintenanceBy = "Ravi", rowVersion = Convert.ToBase64String(pm.RowVersion), completedDate = "2020-01-01", scheduledDate = "2020-01-01",
            status = "Scheduled", pmNo = "HACK", machineId = 2, checklistId = 99,
        })));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var stored = h.Pms.Stored(pm.MachinePmId);
        Assert.Equal(("MPM-0001", "Completed", Today, Today, 1), (stored.PmNo, stored.Status, stored.CompletedDate!.Value, stored.ScheduledDate, stored.MachineId));
    }

    [Fact]
    public async Task PutComplete_Returns400_WithoutMaintenanceBy_OrWhenTooLong_WritingNothing()
    {
        var (data, _, pm) = OneOpen();
        var h = CreateHarness(data);

        var missing = await h.Client.PutAsync($"{Url}/{pm.MachinePmId}/complete", Json(Complete(pm, by: "  ")));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("MaintenanceBy is required.", (await Root(missing)).GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
        var tooLong = await h.Client.PutAsync($"{Url}/{pm.MachinePmId}/complete", Json(Complete(pm, by: new string('x', 101))));
        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsync($"{Url}/{pm.MachinePmId}/complete", Json("{}"))).StatusCode);

        Assert.Equal("Scheduled", h.Pms.Stored(pm.MachinePmId).Status);
        Assert.Single(h.Data.Pms);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task PutComplete_Returns409_ForStaleOrAlreadyCompleted_And404()
    {
        var (data, _, pm) = OneOpen();
        var h = CreateHarness(data);
        var rowVersion = Convert.ToBase64String(pm.RowVersion);

        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PutAsync($"{Url}/99/complete", Json(Complete(pm)))).StatusCode);

        h.Pms.SimulateConcurrentModification(pm.MachinePmId);
        var stale = await h.Client.PutAsync($"{Url}/{pm.MachinePmId}/complete", Json(Complete(pm, rowVersion: rowVersion)));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var text = await stale.Content.ReadAsStringAsync();
        Assert.Contains("The maintenance record was modified by another user.", text);
        Assert.DoesNotContain("Exception", text);
        Assert.Equal("Scheduled", h.Pms.Stored(pm.MachinePmId).Status);
        Assert.Single(h.Data.Pms);

        var fresh = (await Root(await h.Client.GetAsync($"{Url}/{pm.MachinePmId}"))).GetProperty("data").GetProperty("rowVersion").GetString();
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsync($"{Url}/{pm.MachinePmId}/complete", Json(Complete(pm, rowVersion: fresh)))).StatusCode);
        var again = await h.Client.PutAsync($"{Url}/{pm.MachinePmId}/complete", Json(Complete(pm, rowVersion: fresh)));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already completed", await again.Content.ReadAsStringAsync());
        Assert.Equal(2, h.Data.Pms.Count); // the PM + ONE successor
    }

    [Fact]
    public async Task ThereIsNoScheduleEditCancelOrDeleteEndpoint()
    {
        var (data, _, pm) = OneOpen();
        var h = CreateHarness(data);

        var post = await h.Client.PostAsync(Url, Json("""{"machineId":1,"maintenanceTypeId":1,"scheduledDate":"2026-09-25"}"""));
        Assert.Equal(HttpStatusCode.MethodNotAllowed, post.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await h.Client.PutAsync($"{Url}/{pm.MachinePmId}", Json("{}"))).StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, (await h.Client.DeleteAsync($"{Url}/{pm.MachinePmId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PutAsync($"{Url}/{pm.MachinePmId}/start", Json("{}"))).StatusCode);
        Assert.Single(h.Data.Pms);
        Assert.Empty(h.Authorization.Checks);
    }

    // ================================================================ reads

    [Fact]
    public async Task GetAll_FrequencyTabs_PagesTenPerPageByDefault_FiltersByMachine_AndCounts()
    {
        var data = new MachinePmScenario();
        for (var i = 0; i < 12; i++) data.Open(data.Checklist("Daily", Today), Today);
        data.Open(data.Checklist("Weekly", Today), Today);
        data.Open(data.Checklist("Weekly", Today.AddDays(-9), machineId: 2), Today.AddDays(-9)); // overdue - stays in Weekly
        data.Open(data.Checklist("Weekly", Today), Today.AddDays(4));                             // not due yet - hidden
        data.Open(data.Checklist("Monthly", Today), Today);
        data.Open(data.Checklist("Monthly", Today), Today.AddDays(40));                           // not due yet - hidden
        data.Open(data.Checklist("Yearly", Today), Today);
        data.Open(data.Checklist("Yearly", Today), Today.AddDays(200));                           // not due yet - hidden
        data.Completed(data.Checklist("Monthly", Today.AddDays(-3)), Today.AddDays(-3));
        var h = CreateHarness(data);

        var page1 = (await Root(await h.Client.GetAsync($"{Url}?bucket=daily"))).GetProperty("data");
        Assert.Equal((10, 10, 12, 2), (page1.GetProperty("pageSize").GetInt32(), page1.GetProperty("items").GetArrayLength(), page1.GetProperty("totalCount").GetInt32(), page1.GetProperty("totalPages").GetInt32()));
        var page2 = (await Root(await h.Client.GetAsync($"{Url}?bucket=daily&pageNumber=2&pageSize=10"))).GetProperty("data");
        Assert.Equal(2, page2.GetProperty("items").GetArrayLength());

        var weekly = (await Root(await h.Client.GetAsync($"{Url}?bucket=weekly"))).GetProperty("data").GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(new[] { (true, "Weekly"), (false, "Weekly") }, weekly.Select(w => (w.GetProperty("isOverdue").GetBoolean(), w.GetProperty("frequency").GetString()!)));
        var completed = (await Root(await h.Client.GetAsync($"{Url}?bucket=completed"))).GetProperty("data").GetProperty("items").EnumerateArray().Single();
        Assert.Equal(("Completed", "Monthly"), (completed.GetProperty("status").GetString(), completed.GetProperty("frequency").GetString()));

        var counts = (await Root(await h.Client.GetAsync($"{Url}/counts"))).GetProperty("data");
        Assert.Equal((12, 2, 1, 1, 1), (counts.GetProperty("daily").GetInt32(), counts.GetProperty("weekly").GetInt32(), counts.GetProperty("monthly").GetInt32(), counts.GetProperty("yearly").GetInt32(), counts.GetProperty("completed").GetInt32()));
        Assert.False(counts.TryGetProperty("today", out _));
        Assert.False(counts.TryGetProperty("overdue", out _));
        Assert.Equal(1, (await Root(await h.Client.GetAsync($"{Url}?bucket=weekly&machineId=2"))).GetProperty("data").GetProperty("totalCount").GetInt32());
        Assert.Equal(1, (await Root(await h.Client.GetAsync($"{Url}/counts?machineId=2"))).GetProperty("data").GetProperty("weekly").GetInt32());
        Assert.Equal(1, (await Root(await h.Client.GetAsync($"{Url}?bucket=daily&search=mpm-0012"))).GetProperty("data").GetProperty("totalCount").GetInt32());
        Assert.Equal(20, (await Root(await h.Client.GetAsync($"{Url}?pageSize=100"))).GetProperty("data").GetProperty("totalCount").GetInt32()); // no tab: future ones too
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.GetAsync($"{Url}?bucket=today")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.GetAsync($"{Url}?bucket=someday")).StatusCode);
    }

    [Fact]
    public async Task GetById_And404_AndLookupsAreMachinesOnly()
    {
        var (data, _, pm) = OneOpen();
        var h = CreateHarness(data);

        var dto = (await Root(await h.Client.GetAsync($"{Url}/{pm.MachinePmId}"))).GetProperty("data");
        Assert.Equal(("MPM-0001", "Daily", JsonValueKind.Null), (dto.GetProperty("pmNo").GetString(), dto.GetProperty("frequency").GetString(), dto.GetProperty("maintenanceBy").ValueKind));
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.GetAsync($"{Url}/99")).StatusCode);

        var lookups = (await Root(await h.Client.GetAsync($"{Url}/lookups"))).GetProperty("data");
        Assert.Equal(2, lookups.GetProperty("machines").GetArrayLength());
        Assert.False(lookups.TryGetProperty("maintenanceTypes", out _));
        Assert.False(lookups.TryGetProperty("engineers", out _));
    }

    // ================================================================ authorization

    [Fact]
    public async Task Endpoints_Return401_WithoutAToken()
    {
        var (data, _, pm) = OneOpen();
        var h = CreateHarness(data, authenticate: false);

        foreach (var path in new[] { Url, $"{Url}/1", $"{Url}/counts", $"{Url}/lookups" })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync(path)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PutAsync($"{Url}/1/complete", Json(Complete(pm)))).StatusCode);
        Assert.Empty(h.Authorization.Checks);
        Assert.Equal("Scheduled", h.Pms.Stored(pm.MachinePmId).Status);
    }

    [Fact]
    public async Task Endpoints_Return403_WithoutThePermission_AndChangeNothing()
    {
        var (data, _, pm) = OneOpen();
        var h = CreateHarness(data, permissionGranted: false);

        foreach (var path in new[] { Url, $"{Url}/1", $"{Url}/counts", $"{Url}/lookups" })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync(path)).StatusCode);
        }

        var put = await h.Client.PutAsync($"{Url}/1/complete", Json(Complete(pm)));
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
        Assert.Equal("You do not have permission to perform this action.", (await Root(put)).GetProperty("message").GetString());
        Assert.Equal("Scheduled", h.Pms.Stored(pm.MachinePmId).Status);
        Assert.Single(h.Data.Pms);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task EachEndpoint_AsksForTheTrnMachinePmPermission_MatchingItsVerb_CompleteIsEdit()
    {
        var (data, _, pm) = OneOpen();
        var h = CreateHarness(data);

        await h.Client.GetAsync(Url);
        await h.Client.GetAsync($"{Url}/counts");
        await h.Client.GetAsync($"{Url}/lookups");
        await h.Client.GetAsync($"{Url}/1");
        await h.Client.PutAsync($"{Url}/1/complete", Json(Complete(pm)));

        Assert.Equal(
            new[] { PermissionAction.View, PermissionAction.View, PermissionAction.View, PermissionAction.View, PermissionAction.Edit },
            h.Authorization.Checks.Select(c => c.Action));
        Assert.All(h.Authorization.Checks, c => Assert.Equal(ModuleCodes.TrnMachinePm, c.ModuleCode));
    }

    [Fact]
    public async Task ViewWithoutEdit_CanReadButNotComplete_DecidedByTheMatrixNotTheRoleName()
    {
        var (data, _, pm) = OneOpen();
        var h = CreateHarness(data, decide: (_, module, action) => module == ModuleCodes.TrnMachinePm && action == PermissionAction.View);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync($"{Url}/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync($"{Url}/1/complete", Json(Complete(pm)))).StatusCode);
        Assert.Equal("Scheduled", h.Pms.Stored(pm.MachinePmId).Status);
    }

    [Fact]
    public async Task MasterPermissions_DoNotGrantMachinePmAccess()
    {
        var (data, _, pm) = OneOpen();
        var h = CreateHarness(data, decide: (_, module, _) => module is ModuleCodes.MasterMachine or ModuleCodes.MasterMaintenanceType or ModuleCodes.MasterMaintenanceChecklist or ModuleCodes.TrnMoldPm);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync($"{Url}/1/complete", Json(Complete(pm)))).StatusCode);
        Assert.Equal("Scheduled", h.Pms.Stored(pm.MachinePmId).Status);
    }
}
