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
/// /api/v1/maintenance-checklists through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model
/// binding) with the REAL MaintenanceChecklistService. Only persistence is faked. Acting user: id 1 "Sakthi".
/// Checklists: 1 Machine Lubrication (Machine), 2 Mold Cleaning (Mold) - active; 3 Old Checklist (Machine) - inactive.
/// </summary>
public class MaintenanceChecklistEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string Url = "/api/v1/maintenance-checklists";
    private readonly ApiWebApplicationFactory _factory;

    public MaintenanceChecklistEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemoryMaintenanceChecklistRepository Checklists, RecordingAuditLog Audit, StubPermissionAuthorization Authorization);

    private H CreateHarness(bool permissionGranted = true, bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machineList = MachineTestData.Machines();
        var checklists = new InMemoryMaintenanceChecklistRepository(MaintenanceChecklistTestData.Checklists(), machineList);
        var audit = new RecordingAuditLog();
        var authorization = decide is null ? new StubPermissionAuthorization(permissionGranted) : new StubPermissionAuthorization(decide);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMaintenanceChecklistRepository>();
                services.AddSingleton<IMaintenanceChecklistRepository>(checklists);
                services.RemoveAll<IMachineRepository>();
                services.AddSingleton<IMachineRepository>(new InMemoryMachineRepository(machineList, departments, employees));
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

        return new H(client, checklists, audit, authorization);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Root(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static string Rv(H h, int id) => Convert.ToBase64String(h.Checklists.Stored(id).RowVersion);

    private static object[] ItemsOf(params string[] labels) => labels.Select(l => (object)new { itemLabel = l }).ToArray();

    // Recurring configuration: a Machine checklist is Daily on machine 1 (active); the Mold checklist (2) is Weekly with no machine.
    private static readonly string StartToday = new GlobalRubber.MMM.Infrastructure.Services.DateTimeProvider().Today.ToString("yyyy-MM-dd");

    private static string CreateBody(string name = "Hydraulic Inspection", string appliesTo = "Machine", params string[] labels) =>
        JsonSerializer.Serialize(new
        {
            checklistName = name, appliesTo, frequency = "Daily", machineId = appliesTo == "Machine" ? (int?)1 : null, startDate = StartToday,
            items = ItemsOf(labels.Length == 0 ? new[] { "Hoses checked" } : labels),
        });

    private static string UpdateBody(string rowVersion, string name = "Mold Cleaning v2", string appliesTo = "Mold", params string[] labels) =>
        JsonSerializer.Serialize(new
        {
            checklistName = name, appliesTo, frequency = "Weekly", machineId = appliesTo == "Machine" ? (int?)1 : null, startDate = StartToday,
            items = ItemsOf(labels.Length == 0 ? new[] { "Cavities cleaned", "Vents cleared" } : labels), rowVersion,
        });

    // ================================================================ GET

    [Fact]
    public async Task GetAll_Returns200_WithThePagedEnvelope_ItemsIncluded_AndHonoursTheFilters()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync($"{Url}?pageNumber=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Checklists retrieved successfully.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(10, data.GetProperty("pageSize").GetInt32());
        var first = data.GetProperty("items")[0];
        Assert.Equal("CHK-0001", first.GetProperty("checklistCode").GetString());
        Assert.Equal("Machine", first.GetProperty("appliesTo").GetString());
        Assert.Equal(2, first.GetProperty("items").GetArrayLength());
        Assert.Equal("Oil level checked", first.GetProperty("items")[0].GetProperty("itemLabel").GetString());
        Assert.False(string.IsNullOrEmpty(first.GetProperty("rowVersion").GetString()));

        async Task<int> Count(string qs) => (await Root(await h.Client.GetAsync(Url + qs))).GetProperty("data").GetProperty("totalCount").GetInt32();
        Assert.Equal(1, await Count("?search=lubric"));
        Assert.Equal(1, await Count("?appliesTo=Mold"));
        Assert.Equal(2, await Count("?isActive=true"));
    }

    [Fact]
    public async Task GetAll_DefaultsToTenPerPage()
    {
        var h = CreateHarness();
        for (var i = 1; i <= 12; i++)
        {
            Assert.Equal(HttpStatusCode.Created, (await h.Client.PostAsync(Url, Json(CreateBody(name: $"Bulk {i:00}")))).StatusCode);
        }

        var page1 = (await Root(await h.Client.GetAsync(Url))).GetProperty("data");
        Assert.Equal(10, page1.GetProperty("pageSize").GetInt32());
        Assert.Equal(10, page1.GetProperty("items").GetArrayLength());
        Assert.Equal(15, page1.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, page1.GetProperty("totalPages").GetInt32());

        var page2 = (await Root(await h.Client.GetAsync($"{Url}?pageNumber=2&pageSize=10"))).GetProperty("data");
        Assert.Equal(5, page2.GetProperty("items").GetArrayLength());
        Assert.False(page2.GetProperty("hasNextPage").GetBoolean());
    }

    [Fact]
    public async Task GetById_Returns200_And404_ForUnknown()
    {
        var h = CreateHarness();

        var data = (await Root(await h.Client.GetAsync($"{Url}/1"))).GetProperty("data");
        Assert.Equal("Machine Lubrication", data.GetProperty("checklistName").GetString());
        Assert.Equal("Machine", data.GetProperty("appliesTo").GetString());
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
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

        var response = await h.Client.PostAsync(Url, Json(CreateBody(labels: new[] { "Hoses checked", " ", "Pressure recorded" })));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("CHK-0004", data.GetProperty("checklistCode").GetString());
        Assert.True(data.GetProperty("isActive").GetBoolean());
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
        Assert.Equal(4, h.Checklists.Count);
        Assert.Equal(new[] { "ChecklistCreated", "MachinePmScheduled" }, h.Audit.Entries.Select(e => e.Action)); // Machine checklist -> first occurrence
        var entry = h.Audit.Entries[0];
        Assert.Equal("ChecklistCreated", entry.Action);
        Assert.Equal("Sakthi", entry.UserName);
    }

    [Fact]
    public async Task Post_IgnoresClientSuppliedServerControlledFields()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync(Url, Json(
            """{"checklistName":"Belt Check","appliesTo":"Machine","frequency":"Daily","machineId":1,"startDate":"2026-03-01","checklistId":99,"checklistCode":"HACK","isActive":false,"createdBy":42,"rowVersion":"AAAA","items":[{"itemLabel":"Belt tension","checklistItemId":5,"sortOrder":77}]}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = h.Checklists.Stored(4);
        Assert.Equal("CHK-0004", stored.ChecklistCode);
        Assert.True(stored.IsActive);
        Assert.Equal(1, stored.CreatedBy);
        var item = Assert.Single(stored.Items);
        Assert.Equal(1, item.SortOrder);
        Assert.NotEqual(5, item.ChecklistItemId);
    }

    [Fact]
    public async Task Post_Returns400_ForInvalidFields_And409_ForADuplicateActiveName_WritingNothing()
    {
        var h = CreateHarness();

        var invalid = await h.Client.PostAsync(Url, Json("""{"checklistName":" ","appliesTo":"Both","items":[{"itemLabel":"  "}]}"""));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await Root(invalid)).GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("ChecklistName is required.", errors);
        Assert.Contains("AppliesTo must be one of: Machine, Mold.", errors);
        Assert.Contains("Please add at least one checklist item.", errors);

        var duplicate = await h.Client.PostAsync(Url, Json(CreateBody(name: "MACHINE LUBRICATION")));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var text = await duplicate.Content.ReadAsStringAsync();
        Assert.Contains("already exists", text);
        Assert.DoesNotContain("SqlException", text);

        Assert.Equal(3, h.Checklists.Count);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ PUT

    [Fact]
    public async Task Put_Returns200_WithAFreshRowVersion_ReplacesItems_AndNeverChangesCodeOrStatus()
    {
        var h = CreateHarness();
        var oldVersion = Rv(h, 2);

        var response = await h.Client.PutAsync($"{Url}/2", Json(UpdateBody(oldVersion).Replace("\"rowVersion\"", "\"checklistCode\":\"HACK\",\"isActive\":false,\"rowVersion\"")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("Mold Cleaning v2", data.GetProperty("checklistName").GetString());
        Assert.Equal(2, data.GetProperty("items").GetArrayLength());
        Assert.NotEqual(oldVersion, data.GetProperty("rowVersion").GetString());
        Assert.Equal("CHK-0002", h.Checklists.Stored(2).ChecklistCode);
        Assert.True(h.Checklists.Stored(2).IsActive);
        Assert.Equal("ChecklistUpdated", Assert.Single(h.Audit.Entries).Action);
    }

    [Fact]
    public async Task Put_Returns409_ForAStaleRowVersion_WithTheCleanMessage()
    {
        var h = CreateHarness();
        var stale = Rv(h, 2);
        h.Checklists.SimulateConcurrentModification(2);

        var response = await h.Client.PutAsync($"{Url}/2", Json(UpdateBody(stale)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("The checklist was modified by another user. Refresh the checklist and try again.", text);
        Assert.DoesNotContain("DbUpdateConcurrencyException", text);
        Assert.Equal("Mold Cleaning", h.Checklists.Stored(2).ChecklistName);
        Assert.Single(h.Checklists.Stored(2).Items);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns400_404_409_AsAppropriate()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsync($"{Url}/2", Json("""{"checklistName":"A","appliesTo":"Mold","items":[{"itemLabel":"x"}]}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PutAsync($"{Url}/999", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await h.Client.PutAsync($"{Url}/2", Json(UpdateBody(Rv(h, 2), name: "machine lubrication")))).StatusCode);

        Assert.Equal("Mold Cleaning", h.Checklists.Stored(2).ChecklistName);
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
        Assert.Equal("Checklist deactivated successfully.", root.GetProperty("message").GetString());
        Assert.False(root.GetProperty("data").GetProperty("isActive").GetBoolean());
        Assert.False(h.Checklists.Stored(1).IsActive);
        Assert.Equal(2, h.Checklists.Stored(1).Items.Count);
        Assert.Equal(3, h.Checklists.Count);
        Assert.Equal("ChecklistDeactivated", Assert.Single(h.Audit.Entries).Action);

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
        Assert.Equal(3, h.Checklists.Count);
        Assert.True(h.Checklists.Stored(2).IsActive);
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
        Assert.Equal(3, h.Checklists.Count);
        Assert.Equal("Mold Cleaning", h.Checklists.Stored(2).ChecklistName);
        Assert.True(h.Checklists.Stored(2).IsActive);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task EachEndpoint_AsksForTheMasterMaintenanceChecklistPermission_MatchingItsVerb()
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
            Assert.Equal(ModuleCodes.MasterMaintenanceChecklist, c.ModuleCode);
            Assert.Equal("ADMIN", c.RoleCode);
        });
    }

    [Fact]
    public async Task PermissionsAreDecidedPerAction_FromTheMatrix_NotTheRoleName()
    {
        // Even the ADMIN token is refused Delete here, because the (stubbed) permission matrix says so.
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.MasterMaintenanceChecklist && action != PermissionAction.Delete);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await h.Client.PostAsync(Url, Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsync($"{Url}/2", Json(UpdateBody(Rv(h, 2))))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync($"{Url}/2")).StatusCode);
        Assert.True(h.Checklists.Stored(2).IsActive);
    }

    [Fact]
    public async Task AnotherMastersPermission_DoesNotGrantChecklistAccess()
    {
        var h = CreateHarness(decide: (_, module, _) => module is ModuleCodes.MasterMaintenanceType or ModuleCodes.TrnMachinePm);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PostAsync(Url, Json(CreateBody()))).StatusCode);
        Assert.Equal(3, h.Checklists.Count);
    }
}
