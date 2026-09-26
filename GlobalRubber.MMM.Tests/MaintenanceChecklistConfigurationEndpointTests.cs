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
/// Function 2 through the real HTTP pipeline: the frequency/machine fields, their 400/404 responses and
/// GET /api/v1/maintenance-checklists/lookups (MasterMaintenanceChecklist View). Only persistence is faked.
/// </summary>
public class MaintenanceChecklistConfigurationEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string Url = "/api/v1/maintenance-checklists";
    private readonly ApiWebApplicationFactory _factory;

    public MaintenanceChecklistConfigurationEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemoryMaintenanceChecklistRepository Checklists, RecordingAuditLog Audit, StubPermissionAuthorization Authorization);

    private H CreateHarness(bool permissionGranted = true, bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null, string role = "ADMIN")
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
            TestAuth.Authenticate(client, factory, role, 1);
        }

        return new H(client, checklists, audit, authorization);
    }

    private static StringContent Json(object body) => new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Root(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static async Task<List<string?>> Errors(HttpResponseMessage r) => (await Root(r)).GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();

    private static readonly string StartToday = new GlobalRubber.MMM.Infrastructure.Services.DateTimeProvider().Today.ToString("yyyy-MM-dd");

    private static object Body(string appliesTo = "Machine", string? frequency = "Daily", int? machineId = 1, string name = "Injection Machine Daily Inspection", string? startDate = "(today)") =>
        new { checklistName = name, appliesTo, frequency, machineId, startDate = startDate == "(today)" ? StartToday : startDate, items = new[] { new { itemLabel = "Oil level checked" } } };

    // ================================================================ create

    [Fact]
    public async Task Post_MachineChecklist_Returns201_WithFrequencyAndMachine()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync(Url, Json(Body()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("Daily", data.GetProperty("frequency").GetString());
        Assert.Equal(1, data.GetProperty("machineId").GetInt32());
        Assert.Equal("MAC-0001", data.GetProperty("machineCode").GetString());
        Assert.True(data.GetProperty("machineIsActive").GetBoolean());
        Assert.Equal(new[] { "ChecklistCreated", "MachinePmScheduled" }, h.Audit.Entries.Select(e => e.Action)); // Function 3: first occurrence
    }

    [Fact]
    public async Task Post_MoldChecklist_Returns201_WithNoMachine()
    {
        var h = CreateHarness();

        var data = (await Root(await h.Client.PostAsync(Url, Json(Body(appliesTo: "Mold", machineId: null, name: "Mold Clean"))))).GetProperty("data");

        Assert.Equal("Daily", data.GetProperty("frequency").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("machineId").ValueKind);
    }

    [Fact]
    public async Task Post_InvalidConfigurations_Return400_Or404_WritingNothing()
    {
        var h = CreateHarness();

        var noFrequency = await h.Client.PostAsync(Url, Json(Body(frequency: null)));
        Assert.Equal(HttpStatusCode.BadRequest, noFrequency.StatusCode);
        Assert.Contains("Frequency is required.", await Errors(noFrequency));

        var badFrequency = await h.Client.PostAsync(Url, Json(Body(frequency: "Hourly")));
        Assert.Contains("Frequency must be one of: Daily, Weekly, Monthly, Yearly.", await Errors(badFrequency));

        var noMachine = await h.Client.PostAsync(Url, Json(Body(machineId: null)));
        Assert.Contains("MachineId is required for a Machine checklist.", await Errors(noMachine));

        var inactive = await h.Client.PostAsync(Url, Json(Body(machineId: 3)));
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Contains("The selected machine is not active.", await Errors(inactive));

        var moldWithMachine = await h.Client.PostAsync(Url, Json(Body(appliesTo: "Mold", machineId: 1)));
        Assert.Contains("A Mold checklist cannot have a machine.", await Errors(moldWithMachine));

        var unknown = await h.Client.PostAsync(Url, Json(Body(machineId: 999)));
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.DoesNotContain("Exception", await unknown.Content.ReadAsStringAsync());

        Assert.Equal(3, h.Checklists.Count);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ Function 3: first occurrence through POST

    [Fact]
    public async Task Post_MachineChecklist_CreatesItsFirstOccurrence_AndStillReturnsTheChecklist()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync(Url, Json(Body(machineId: 2)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("CHK-0004", data.GetProperty("checklistCode").GetString());
        Assert.False(data.TryGetProperty("pmNo", out _)); // the response is the checklist, not a scheduling result
        var occurrence = Assert.Single(h.Checklists.Pms);
        Assert.Equal((2, "Scheduled", 1), (occurrence.MachineId, occurrence.Status, occurrence.ChecklistItems.Count));
        Assert.Null(occurrence.MaintenanceTypeId);
    }

    [Fact]
    public async Task Post_MoldChecklist_CreatesNoOccurrence()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.Created, (await h.Client.PostAsync(Url, Json(Body(appliesTo: "Mold", machineId: null, name: "Mold Clean")))).StatusCode);

        Assert.Empty(h.Checklists.Pms);
    }

    [Fact]
    public async Task Post_WithoutAddPermission_Is403_AndCreatesNoOccurrence()
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.MasterMaintenanceChecklist && action == PermissionAction.View);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PostAsync(Url, Json(Body()))).StatusCode);
        Assert.Empty(h.Checklists.Pms);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Post_AnInvalidMachineChecklist_CreatesNoOccurrence()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PostAsync(Url, Json(Body(machineId: 3)))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PostAsync(Url, Json(Body(machineId: 999)))).StatusCode);
        Assert.Empty(h.Checklists.Pms);
    }

    // ================================================================ Function 4: edits keep the open occurrence in step

    private static async Task<(int Id, string RowVersion)> PostMachineChecklist(H h)
    {
        var data = (await Root(await h.Client.PostAsync(Url, Json(Body(machineId: 2))))).GetProperty("data");
        return (data.GetProperty("checklistId").GetInt32(), data.GetProperty("rowVersion").GetString()!);
    }

    private static object UpdateBodyF4(string rowVersion, string frequency = "Daily", int? machineId = 2, string appliesTo = "Machine", params string[] labels) =>
        new { checklistName = "Injection Machine Daily Inspection", appliesTo, frequency, machineId, startDate = StartToday, items = (labels.Length == 0 ? new[] { "Oil level checked" } : labels).Select(l => new { itemLabel = l }), rowVersion };

    [Fact]
    public async Task Put_FrequencyMachineAndItems_MovesAndReDatesTheSameOccurrence()
    {
        var h = CreateHarness();
        var (id, rowVersion) = await PostMachineChecklist(h);
        var pmNo = Assert.Single(h.Checklists.Pms).PmNo;

        var response = await h.Client.PutAsync($"{Url}/{id}", Json(UpdateBodyF4(rowVersion, frequency: "Weekly", machineId: 1, labels: new[] { "Belts checked", "Oil level checked" })));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var open = Assert.Single(h.Checklists.Pms);
        Assert.Equal((pmNo, 1), (open.PmNo, open.MachineId));
        Assert.Equal(new[] { "Belts checked", "Oil level checked" }, open.ChecklistItems.Select(l => l.ItemLabel));
        Assert.Equal("ChecklistUpdated", h.Audit.Entries.Last().Action);
        Assert.Contains(h.Audit.Entries.Last().Details, d => d.FieldName == $"open_pm_machine ({pmNo})");
    }

    [Fact]
    public async Task Put_StaleRowVersion_Is409_AndLeavesTheOccurrenceAlone()
    {
        var h = CreateHarness();
        var (id, rowVersion) = await PostMachineChecklist(h);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsync($"{Url}/{id}", Json(UpdateBodyF4(rowVersion, frequency: "Monthly")))).StatusCode);
        var afterFirst = (h.Checklists.Pms.Single().MachineId, h.Checklists.Pms.Single().ScheduledDate);

        var stale = await h.Client.PutAsync($"{Url}/{id}", Json(UpdateBodyF4(rowVersion, machineId: 1)));

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(afterFirst, (h.Checklists.Pms.Single().MachineId, h.Checklists.Pms.Single().ScheduledDate));
    }

    [Fact]
    public async Task Put_WithoutEditPermission_Is403_AndChangesNothing()
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.MasterMaintenanceChecklist && action != PermissionAction.Edit);
        var (id, rowVersion) = await PostMachineChecklist(h);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync($"{Url}/{id}", Json(UpdateBodyF4(rowVersion, machineId: 1)))).StatusCode);
        Assert.Equal(2, h.Checklists.Pms.Single().MachineId);
    }

    [Fact]
    public async Task Delete_Deactivates_AndKeepsTheOpenOccurrence()
    {
        var h = CreateHarness();
        var (id, _) = await PostMachineChecklist(h);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.DeleteAsync($"{Url}/{id}")).StatusCode);

        Assert.False(h.Checklists.Stored(id).IsActive);
        Assert.Equal("Scheduled", Assert.Single(h.Checklists.Pms).Status);
    }

    // ================================================================ update: inactive legacy checklist

    [Fact]
    public async Task Put_InactiveLegacyChecklist_WithNoFrequencyOrMachine_Returns200()
    {
        var h = CreateHarness();
        var rowVersion = Convert.ToBase64String(h.Checklists.Stored(3).RowVersion);

        var response = await h.Client.PutAsync($"{Url}/3", Json(new
        {
            checklistName = "Old Checklist", appliesTo = "Machine", frequency = (string?)null, machineId = (int?)null,
            items = new[] { new { itemLabel = "Legacy step" } }, rowVersion,
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.False(data.GetProperty("isActive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("frequency").ValueKind);
    }

    // ================================================================ lookups (11, 12, 14, 15)

    [Fact]
    public async Task GetLookups_Returns200_ActiveMachinesOnly_WithTheMinimalFields()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync($"{Url}/lookups");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Checklist lookups retrieved successfully.", root.GetProperty("message").GetString());
        var machines = root.GetProperty("data").GetProperty("machines").EnumerateArray().ToList();
        Assert.Equal(new[] { "MAC-0001", "MAC-0002" }, machines.Select(m => m.GetProperty("machineCode").GetString()));
        Assert.All(machines, m => Assert.Equal(new[] { "machineId", "machineCode", "machineName" }, m.EnumerateObject().Select(p => p.Name)));
    }

    [Fact]
    public async Task GetLookups_AsksForMasterMaintenanceChecklistView()
    {
        var h = CreateHarness();

        await h.Client.GetAsync($"{Url}/lookups");

        var check = Assert.Single(h.Authorization.Checks);
        Assert.Equal((ModuleCodes.MasterMaintenanceChecklist, PermissionAction.View), (check.ModuleCode, check.Action));
    }

    [Fact]
    public async Task GetLookups_Is401WithoutAToken_And403WithoutThePermission()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await CreateHarness(authenticate: false).Client.GetAsync($"{Url}/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await CreateHarness(permissionGranted: false).Client.GetAsync($"{Url}/lookups")).StatusCode);
    }

    [Fact]
    public async Task GetLookups_IsDecidedByThePermissionMatrix_NotTheRoleName()
    {
        // A non-admin role granted only checklist View gets the lookups; the ADMIN role without it does not.
        var viewOnly = CreateHarness(decide: (_, module, action) => module == ModuleCodes.MasterMaintenanceChecklist && action == PermissionAction.View, role: "PRODUCTION_USER");
        Assert.Equal(HttpStatusCode.OK, (await viewOnly.Client.GetAsync($"{Url}/lookups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewOnly.Client.PostAsync(Url, Json(Body()))).StatusCode);

        var adminWithoutIt = CreateHarness(decide: (_, module, _) => module == ModuleCodes.MasterMachine);
        Assert.Equal(HttpStatusCode.Forbidden, (await adminWithoutIt.Client.GetAsync($"{Url}/lookups")).StatusCode);
    }

    // ================================================================ start date (migration 013)

    [Fact]
    public async Task Post_StartDate_IsRequiredForAMachineChecklist_ReturnedAsYyyyMmDd_AndIsTheFirstDueDate()
    {
        var h = CreateHarness();

        var missing = await h.Client.PostAsync(Url, Json(Body(startDate: null)));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Contains("StartDate is required for a Machine checklist.", await Errors(missing));
        Assert.Empty(h.Checklists.Pms.Where(p => p.PmNo == "MPM-0007"));

        var created = await h.Client.PostAsync(Url, Json(Body(startDate: "2026-12-31")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var data = (await Root(created)).GetProperty("data");
        Assert.Equal("2026-12-31", data.GetProperty("startDate").GetString());
        var id = data.GetProperty("checklistId").GetInt32();
        Assert.Equal(new DateOnly(2026, 12, 31), Assert.Single(h.Checklists.Pms, p => p.Status == "Scheduled" && p.ChecklistId == id).ScheduledDate);

        var mold = await h.Client.PostAsync(Url, Json(Body(appliesTo: "Mold", machineId: null, name: "Mold Clean", startDate: null)));
        Assert.Equal(HttpStatusCode.Created, mold.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await Root(mold)).GetProperty("data").GetProperty("startDate").ValueKind);
    }
}
