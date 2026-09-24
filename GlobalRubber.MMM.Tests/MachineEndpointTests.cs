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
/// /api/v1/machines through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model binding) with
/// the REAL MachineService. Only persistence is faked. Acting user: id 1 "Sakthi". See <see cref="MachineTestData"/>.
/// </summary>
public class MachineEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public MachineEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemoryMachineRepository Machines, RecordingAuditLog Audit, StubPermissionAuthorization Authorization);

    private H CreateHarness(bool permissionGranted = true, bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var machines = new InMemoryMachineRepository(MachineTestData.Machines(), departments, employees);
        var audit = new RecordingAuditLog();
        var authorization = decide is null ? new StubPermissionAuthorization(permissionGranted) : new StubPermissionAuthorization(decide);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMachineRepository>();
                services.AddSingleton<IMachineRepository>(machines);
                services.RemoveAll<IDepartmentRepository>();
                services.AddSingleton<IDepartmentRepository>(departments);
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

        return new H(client, machines, audit, authorization);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Root(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static string Rv(H h, int id) => Convert.ToBase64String(h.Machines.Stored(id).RowVersion);
    private static List<string?> Errors(JsonElement root) => root.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();

    private static string CreateBody(string name = "Banbury Mixer", int departmentId = 2, string? serial = "BR270-5521") =>
        JsonSerializer.Serialize(new
        {
            machineName = name, machineType = "Mixer", departmentId, location = "Shop Floor 2", manufacturer = "Farrel", model = "BR-270",
            serialNumber = serial, capacity = "270 Litre", installationDate = "2017-05-10", maintenanceFrequencyDays = 45,
            criticality = "High", remarks = "New",
        });

    private static string UpdateBody(string rowVersion, string name = "Injection Moulding M/c 1A", int departmentId = 1, string? serial = "LTD250-1145") =>
        JsonSerializer.Serialize(new
        {
            machineName = name, machineType = "Injection Moulding", departmentId, location = "Bay 1", serialNumber = serial,
            installationDate = "2019-03-14", maintenanceFrequencyDays = 30, criticality = "High", rowVersion,
        });

    // ================================================================ GET

    [Fact]
    public async Task GetAll_Returns200_WithThePagedEnvelope_AndJoinedNames()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync("/api/v1/machines?pageNumber=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Machines retrieved successfully.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        var first = data.GetProperty("items")[0];
        Assert.Equal("MAC-0001", first.GetProperty("machineCode").GetString());
        Assert.Equal("Injection Moulding", first.GetProperty("departmentName").GetString());
        Assert.False(first.TryGetProperty("responsibleEngineerId", out _));
        Assert.False(first.TryGetProperty("responsibleEngineerName", out _));
        Assert.False(first.TryGetProperty("responsibleEngineerIsActive", out _));
        Assert.Equal("Running", first.GetProperty("operationalStatus").GetString());
        Assert.Equal("2019-03-14", first.GetProperty("installationDate").GetString());
        Assert.False(string.IsNullOrEmpty(first.GetProperty("rowVersion").GetString()));
    }

    [Fact]
    public async Task GetAll_HonoursTheFilters()
    {
        var h = CreateHarness();
        async Task<int> Count(string qs) => (await Root(await h.Client.GetAsync("/api/v1/machines" + qs))).GetProperty("data").GetProperty("totalCount").GetInt32();

        Assert.Equal(1, await Count("?search=mixing"));
        Assert.Equal(1, await Count("?departmentId=3"));
        Assert.Equal(1, await Count("?operationalStatus=Breakdown"));
        Assert.Equal(1, await Count("?isActive=false"));
        Assert.Equal(2, await Count("?isActive=true"));
    }

    [Fact]
    public async Task GetById_Returns200_WithAllFields_And404_ForUnknown()
    {
        var h = CreateHarness();

        var data = (await Root(await h.Client.GetAsync("/api/v1/machines/1"))).GetProperty("data");
        Assert.Equal("LTD250-1145", data.GetProperty("serialNumber").GetString());
        Assert.Equal(30, data.GetProperty("maintenanceFrequencyDays").GetInt32());
        Assert.Equal("2026-03-03", data.GetProperty("nextMaintenanceDate").GetString());
        Assert.Equal(Rv(h, 1), data.GetProperty("rowVersion").GetString());

        var missing = await h.Client.GetAsync("/api/v1/machines/999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.False((await Root(missing)).GetProperty("success").GetBoolean());
    }

    // ================================================================ POST

    [Fact]
    public async Task Post_Returns201_Running_Active_WithTheCode_AndAudits()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/machines", Json(CreateBody()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("MAC-0004", data.GetProperty("machineCode").GetString());
        Assert.Equal("Running", data.GetProperty("operationalStatus").GetString());
        Assert.Equal("2017-05-10", data.GetProperty("installationDate").GetString());
        Assert.True(data.GetProperty("isActive").GetBoolean());
        Assert.Equal(4, h.Machines.Count);
        Assert.Equal("MachineCreated", Assert.Single(h.Audit.Entries).Action);
    }

    [Fact]
    public async Task Post_IgnoresClientSuppliedSystemManagedFields()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/machines", Json(
            """{"machineName":"X","machineType":"T","departmentId":1,"location":"L","maintenanceFrequencyDays":30,"criticality":"Low","machineId":99,"machineCode":"HACK-9","isActive":false,"operationalStatus":"Breakdown","lastMaintenanceDate":"2020-01-01","nextMaintenanceDate":"2020-02-01","createdBy":42,"rowVersion":"AAAA"}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = h.Machines.Stored(4);
        Assert.Equal("MAC-0004", stored.MachineCode);
        Assert.True(stored.IsActive);
        Assert.Equal("Running", stored.OperationalStatus);
        Assert.Null(stored.LastMaintenanceDate);
        Assert.Null(stored.NextMaintenanceDate);
        Assert.Equal(1, stored.CreatedBy);
    }

    [Fact]
    public async Task Post_Returns400_404_409_AsAppropriate_WritingNothing()
    {
        var h = CreateHarness();

        var invalid = await h.Client.PostAsync("/api/v1/machines", Json("""{"machineName":" ","departmentId":0,"maintenanceFrequencyDays":0,"criticality":"Urgent"}"""));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = Errors(await Root(invalid));
        Assert.Contains("MachineName is required.", errors);
        Assert.Contains("MachineType is required.", errors);
        Assert.Contains("DepartmentId is required.", errors);
        Assert.Contains("Location is required.", errors);
        Assert.Contains("MaintenanceFrequencyDays must be greater than 0.", errors);
        Assert.Contains("Criticality must be one of: Low, Medium, High.", errors);

        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PostAsync("/api/v1/machines", Json(CreateBody(departmentId: 999)))).StatusCode);

        var inactiveDept = await h.Client.PostAsync("/api/v1/machines", Json(CreateBody(departmentId: 3)));
        Assert.Equal(HttpStatusCode.BadRequest, inactiveDept.StatusCode);
        Assert.Contains("The selected department is not active.", Errors(await Root(inactiveDept)));

        var duplicateName = await h.Client.PostAsync("/api/v1/machines", Json(CreateBody(name: "MIXING MILL")));
        Assert.Equal(HttpStatusCode.Conflict, duplicateName.StatusCode);
        var duplicateSerial = await h.Client.PostAsync("/api/v1/machines", Json(CreateBody(serial: "LTD250-1145")));
        Assert.Equal(HttpStatusCode.Conflict, duplicateSerial.StatusCode);
        Assert.DoesNotContain("UX_machine_master", await duplicateSerial.Content.ReadAsStringAsync());

        Assert.Equal(3, h.Machines.Count);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ Responsible Engineer (temporarily disabled)

    [Fact]
    public async Task Post_IgnoresAResponsibleEngineerInTheBody_EvenAnUnknownOrInactiveOne()
    {
        var h = CreateHarness();

        var unknown = await h.Client.PostAsync("/api/v1/machines", Json(CreateBody().Replace("\"criticality\"", "\"responsibleEngineerId\":999,\"criticality\"")));
        var inactive = await h.Client.PostAsync("/api/v1/machines", Json(CreateBody(name: "Other", serial: null).Replace("\"criticality\"", "\"responsibleEngineerId\":3,\"criticality\"")));

        Assert.Equal(HttpStatusCode.Created, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Created, inactive.StatusCode);
        Assert.Null(h.Machines.Stored(4).ResponsibleEngineerId);
        Assert.Null(h.Machines.Stored(5).ResponsibleEngineerId);
        Assert.DoesNotContain("responsibleEngineer", await unknown.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Put_NeverChangesOrClearsTheStoredResponsibleEngineer()
    {
        var h = CreateHarness();

        var withOther = await h.Client.PutAsync("/api/v1/machines/1", Json(UpdateBody(Rv(h, 1)).Replace("\"criticality\"", "\"responsibleEngineerId\":2,\"criticality\"")));
        var withNull = await h.Client.PutAsync("/api/v1/machines/1", Json(UpdateBody(Rv(h, 1)).Replace("\"criticality\"", "\"responsibleEngineerId\":null,\"criticality\"")));
        var without = await h.Client.PutAsync("/api/v1/machines/1", Json(UpdateBody(Rv(h, 1), name: "Renamed")));

        Assert.Equal(HttpStatusCode.OK, withOther.StatusCode);
        Assert.Equal(HttpStatusCode.OK, withNull.StatusCode);
        Assert.Equal(HttpStatusCode.OK, without.StatusCode);
        Assert.Equal(1, h.Machines.Stored(1).ResponsibleEngineerId); // the value already in the database is untouched
        Assert.Equal("Renamed", h.Machines.Stored(1).MachineName);
        Assert.All(h.Audit.Entries, e => Assert.DoesNotContain(e.Details, d => d.FieldName.Contains("engineer", StringComparison.OrdinalIgnoreCase)));
    }

    // ================================================================ PUT

    [Fact]
    public async Task Put_Returns200_WithAFreshRowVersion_AndNeverChangesSystemManagedFields()
    {
        var h = CreateHarness();
        var oldVersion = Rv(h, 1);

        var response = await h.Client.PutAsync("/api/v1/machines/1", Json(UpdateBody(oldVersion).Replace("\"rowVersion\"", "\"operationalStatus\":\"Breakdown\",\"isActive\":false,\"machineCode\":\"HACK\",\"rowVersion\"")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("Injection Moulding M/c 1A", data.GetProperty("machineName").GetString());
        Assert.NotEqual(oldVersion, data.GetProperty("rowVersion").GetString());
        var stored = h.Machines.Stored(1);
        Assert.Equal("MAC-0001", stored.MachineCode);
        Assert.Equal("Running", stored.OperationalStatus);
        Assert.True(stored.IsActive);
        Assert.Equal(new DateOnly(2026, 2, 1), stored.LastMaintenanceDate);
        Assert.Equal("MachineUpdated", Assert.Single(h.Audit.Entries).Action);
    }

    [Fact]
    public async Task Put_Returns409_ForAStaleRowVersion_WithACleanMessage_AndOverwritesNothing()
    {
        var h = CreateHarness();
        var stale = Rv(h, 1);
        h.Machines.SimulateConcurrentModification(1);

        var response = await h.Client.PutAsync("/api/v1/machines/1", Json(UpdateBody(stale)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("The machine was modified by another user. Refresh the machine and try again.", text);
        Assert.DoesNotContain("DbUpdateConcurrencyException", text);
        Assert.Equal("Injection Moulding M/c 1", h.Machines.Stored(1).MachineName);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns400_404_409_AsAppropriate()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsync("/api/v1/machines/1", Json("""{"machineName":"A","machineType":"T","departmentId":1,"location":"L","maintenanceFrequencyDays":30,"criticality":"Low"}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PutAsync("/api/v1/machines/999", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsync("/api/v1/machines/1", Json(UpdateBody(Rv(h, 1), departmentId: 3)))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await h.Client.PutAsync("/api/v1/machines/1", Json(UpdateBody(Rv(h, 1), name: "mixing mill")))).StatusCode);

        Assert.Equal("Injection Moulding M/c 1", h.Machines.Stored(1).MachineName);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ DELETE (soft deactivation)

    [Fact]
    public async Task Delete_Returns200_Deactivates_KeepsTheRow_AndAudits_And409_ThereAfter()
    {
        var h = CreateHarness();

        var response = await h.Client.DeleteAsync("/api/v1/machines/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Machine deactivated successfully.", root.GetProperty("message").GetString());
        Assert.False(root.GetProperty("data").GetProperty("isActive").GetBoolean());
        Assert.False(h.Machines.Stored(1).IsActive);
        Assert.Equal(3, h.Machines.Count);
        Assert.Equal("MachineDeactivated", Assert.Single(h.Audit.Entries).Action);

        var again = await h.Client.DeleteAsync("/api/v1/machines/1");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.DeleteAsync("/api/v1/machines/999")).StatusCode);
        Assert.Single(h.Audit.Entries);
    }

    // ================================================================ authorization

    [Fact]
    public async Task Machines_Endpoints_Return401_WithoutAToken_AndChangeNothing()
    {
        var h = CreateHarness(authenticate: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/machines")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/machines/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PostAsync("/api/v1/machines", Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PutAsync("/api/v1/machines/1", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.DeleteAsync("/api/v1/machines/1")).StatusCode);
        Assert.Equal(3, h.Machines.Count);
        Assert.True(h.Machines.Stored(1).IsActive);
        Assert.Empty(h.Authorization.Checks);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Machines_Endpoints_Return403_WithoutThePermission_AndChangeNothing()
    {
        var h = CreateHarness(permissionGranted: false);

        var post = await h.Client.PostAsync("/api/v1/machines", Json(CreateBody()));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/machines")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/machines/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync("/api/v1/machines/1", Json(UpdateBody(Rv(h, 1))))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync("/api/v1/machines/1")).StatusCode);

        Assert.Equal("You do not have permission to perform this action.", (await Root(post)).GetProperty("message").GetString());
        Assert.Equal(3, h.Machines.Count);
        Assert.Equal("Injection Moulding M/c 1", h.Machines.Stored(1).MachineName);
        Assert.True(h.Machines.Stored(1).IsActive);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task EachEndpoint_AsksForTheMasterMachinePermission_MatchingItsVerb()
    {
        var h = CreateHarness();

        await h.Client.GetAsync("/api/v1/machines");
        await h.Client.GetAsync("/api/v1/machines/1");
        await h.Client.PostAsync("/api/v1/machines", Json(CreateBody()));
        await h.Client.PutAsync("/api/v1/machines/1", Json(UpdateBody(Rv(h, 1))));
        await h.Client.DeleteAsync("/api/v1/machines/1");

        Assert.Equal(
            new[] { PermissionAction.View, PermissionAction.View, PermissionAction.Add, PermissionAction.Edit, PermissionAction.Delete },
            h.Authorization.Checks.Select(c => c.Action));
        Assert.All(h.Authorization.Checks, c =>
        {
            Assert.Equal(ModuleCodes.MasterMachine, c.ModuleCode);
            Assert.Equal("ADMIN", c.RoleCode);
        });
    }

    [Fact]
    public async Task ViewOnly_CanReadButNotWrite_DecidedByTheMatrixNotTheRoleName()
    {
        // e.g. PRODUCTION_USER / MAINT_ENGINEER in the seed: Machine View only. Even the ADMIN token is refused writes
        // here, because the (stubbed) permission matrix says so.
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.MasterMachine && action == PermissionAction.View);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync("/api/v1/machines")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync("/api/v1/machines/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PostAsync("/api/v1/machines", Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync("/api/v1/machines/1", Json(UpdateBody(Rv(h, 1))))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync("/api/v1/machines/1")).StatusCode);
        Assert.Equal(3, h.Machines.Count);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task DepartmentOrEmployeePermissions_DoNotGrantMachineAccess()
    {
        var h = CreateHarness(decide: (_, module, _) => module is ModuleCodes.MasterDepartment or ModuleCodes.MasterEmployee);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/machines")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PostAsync("/api/v1/machines", Json(CreateBody()))).StatusCode);
        Assert.Equal(3, h.Machines.Count);
    }
}
