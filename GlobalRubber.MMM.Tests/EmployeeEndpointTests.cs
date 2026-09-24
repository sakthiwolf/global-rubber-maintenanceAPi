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
/// /api/v1/employees through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model binding)
/// with the REAL EmployeeService. Only persistence is faked. Acting user: id 1 "Sakthi". Departments 1, 2 active, 3
/// inactive; employees 1 Ravi Kumar, 2 Karthik Raja (in the inactive department), 3 Old Hand (inactive).
/// </summary>
public class EmployeeEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public EmployeeEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemoryEmployeeRepository Employees, RecordingAuditLog Audit, StubPermissionAuthorization Authorization);

    private H CreateHarness(bool permissionGranted = true, bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var employees = new InMemoryEmployeeRepository(EmployeeTestData.Employees(), departments);
        var audit = new RecordingAuditLog();
        var authorization = decide is null ? new StubPermissionAuthorization(permissionGranted) : new StubPermissionAuthorization(decide);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEmployeeRepository>();
                services.AddSingleton<IEmployeeRepository>(employees);
                services.RemoveAll<IDepartmentRepository>();
                services.AddSingleton<IDepartmentRepository>(departments);
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

        return new H(client, employees, audit, authorization);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Root(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static string Rv(H h, int id) => Convert.ToBase64String(h.Employees.Stored(id).RowVersion);

    private static string CreateBody(string name = "Priya Dharshini", string designation = "QC Inspector", int departmentId = 2) =>
        JsonSerializer.Serialize(new { employeeName = name, designation, departmentId, mobile = "9840011128", email = "priya@example.com" });

    private static string UpdateBody(string rowVersion, string name = "Ravi K", int departmentId = 1) =>
        JsonSerializer.Serialize(new { employeeName = name, designation = "Maintenance Engineer", departmentId, mobile = "9840011122", email = "ravi@example.com", rowVersion });

    private static List<string?> Errors(JsonElement root) => root.GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();

    // ================================================================ GET

    [Fact]
    public async Task GetAll_Returns200_WithThePagedEnvelope_AndDepartmentNames()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync("/api/v1/employees?pageNumber=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Employees retrieved successfully.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        var first = data.GetProperty("items")[0];
        Assert.Equal("Karthik Raja", first.GetProperty("employeeName").GetString());
        Assert.Equal("Old Dept", first.GetProperty("departmentName").GetString());
        Assert.False(first.GetProperty("departmentIsActive").GetBoolean());
        Assert.False(string.IsNullOrEmpty(first.GetProperty("rowVersion").GetString()));
    }

    [Fact]
    public async Task GetAll_HonoursTheSearchAndIsActiveFilters()
    {
        var h = CreateHarness();

        var search = (await Root(await h.Client.GetAsync("/api/v1/employees?search=ravi"))).GetProperty("data");
        var inactive = (await Root(await h.Client.GetAsync("/api/v1/employees?isActive=false"))).GetProperty("data");

        Assert.Equal("Ravi Kumar", search.GetProperty("items")[0].GetProperty("employeeName").GetString());
        Assert.Equal(1, search.GetProperty("totalCount").GetInt32());
        Assert.Equal("Old Hand", inactive.GetProperty("items")[0].GetProperty("employeeName").GetString());
    }

    [Fact]
    public async Task GetById_Returns200_WithAllFields_And404_ForUnknown()
    {
        var h = CreateHarness();

        var data = (await Root(await h.Client.GetAsync("/api/v1/employees/1"))).GetProperty("data");
        Assert.Equal("EMP-0001", data.GetProperty("employeeCode").GetString());
        Assert.Equal("Maintenance Engineer", data.GetProperty("designation").GetString());
        Assert.Equal(1, data.GetProperty("departmentId").GetInt32());
        Assert.Equal("Injection Moulding", data.GetProperty("departmentName").GetString());
        Assert.Equal("9840011122", data.GetProperty("mobile").GetString());
        Assert.Equal("ravi@example.com", data.GetProperty("email").GetString());
        Assert.Equal(1, data.GetProperty("createdBy").GetInt32());
        Assert.Equal(Rv(h, 1), data.GetProperty("rowVersion").GetString());

        var missing = await h.Client.GetAsync("/api/v1/employees/999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.False((await Root(missing)).GetProperty("success").GetBoolean());
    }

    // ================================================================ POST

    [Fact]
    public async Task Post_Returns201_WithTheEmployee_AndALocationHeader_AndAudits()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/employees", Json(CreateBody()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("EMP-0004", data.GetProperty("employeeCode").GetString());
        Assert.Equal("Mixing", data.GetProperty("departmentName").GetString());
        Assert.True(data.GetProperty("isActive").GetBoolean());
        Assert.Equal(4, h.Employees.Count);

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal("EmployeeCreated", entry.Action);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Equal("EMP-0004", entry.RecordRef);
    }

    [Fact]
    public async Task Post_IgnoresClientSuppliedServerControlledFields()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/employees", Json(
            """{"employeeName":"X","designation":"Y","departmentId":1,"employeeId":99,"employeeCode":"HACK-9","isActive":false,"createdBy":42,"createdAt":"2000-01-01T00:00:00Z","rowVersion":"AAAA","updatedBy":7}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = h.Employees.Stored(4);
        Assert.Equal("EMP-0004", stored.EmployeeCode);
        Assert.True(stored.IsActive);
        Assert.Equal(1, stored.CreatedBy); // the token's user, not 42
        Assert.NotEqual(new DateTime(2000, 1, 1), stored.CreatedAt.Date); // server clock, not the client's value
        Assert.Null(stored.UpdatedBy);
    }

    [Fact]
    public async Task Post_Returns400_ForInvalidFields_404_ForUnknownDepartment_400_ForInactiveDepartment_WritingNothing()
    {
        var h = CreateHarness();

        var invalid = await h.Client.PostAsync("/api/v1/employees", Json(CreateBody(name: " ", designation: "", departmentId: 0)));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = Errors(await Root(invalid));
        Assert.Contains("EmployeeName is required.", errors);
        Assert.Contains("Designation is required.", errors);
        Assert.Contains("DepartmentId is required.", errors);

        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PostAsync("/api/v1/employees", Json(CreateBody(departmentId: 999)))).StatusCode);

        var inactive = await h.Client.PostAsync("/api/v1/employees", Json(CreateBody(departmentId: 3)));
        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Contains("The selected department is not active.", Errors(await Root(inactive)));

        Assert.Equal(3, h.Employees.Count);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ PUT

    [Fact]
    public async Task Put_Returns200_WithTheUpdatedEmployee_AndAFreshRowVersion()
    {
        var h = CreateHarness();
        var oldVersion = Rv(h, 1);

        var response = await h.Client.PutAsync("/api/v1/employees/1", Json(UpdateBody(oldVersion, departmentId: 2)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("Ravi K", data.GetProperty("employeeName").GetString());
        Assert.Equal("Mixing", data.GetProperty("departmentName").GetString());
        Assert.Equal("EMP-0001", data.GetProperty("employeeCode").GetString());
        Assert.NotEqual(oldVersion, data.GetProperty("rowVersion").GetString());
        Assert.Equal(1, h.Employees.Stored(1).UpdatedBy);
        Assert.Equal("EmployeeUpdated", Assert.Single(h.Audit.Entries).Action);
    }

    [Fact]
    public async Task Put_IgnoresACodeOrStatusInTheBody()
    {
        var h = CreateHarness();

        var response = await h.Client.PutAsync("/api/v1/employees/1", Json(
            $$"""{"employeeName":"Ravi Kumar","designation":"Maintenance Engineer","departmentId":1,"employeeCode":"HACK-9","isActive":false,"createdBy":42,"rowVersion":"{{Rv(h, 1)}}"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("EMP-0001", h.Employees.Stored(1).EmployeeCode);
        Assert.True(h.Employees.Stored(1).IsActive);
        Assert.Equal(1, h.Employees.Stored(1).CreatedBy);
    }

    [Fact]
    public async Task Put_Returns409_ForAStaleRowVersion_WithACleanMessage_AndOverwritesNothing()
    {
        var h = CreateHarness();
        var stale = Rv(h, 1);
        h.Employees.SimulateConcurrentModification(1);

        var response = await h.Client.PutAsync("/api/v1/employees/1", Json(UpdateBody(stale)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("modified by another user", text);
        Assert.DoesNotContain("DbUpdateConcurrencyException", text);
        Assert.Equal("Ravi Kumar", h.Employees.Stored(1).EmployeeName);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns400_ForMissingRowVersion_404_ForUnknownEmployee_400_ForMovingIntoAnInactiveDepartment()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsync("/api/v1/employees/1", Json("""{"employeeName":"A","designation":"B","departmentId":1}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PutAsync("/api/v1/employees/999", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsync("/api/v1/employees/1", Json(UpdateBody(Rv(h, 1), departmentId: 3)))).StatusCode);

        Assert.Equal(1, h.Employees.Stored(1).DepartmentId);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ DELETE (soft deactivation)

    [Fact]
    public async Task Delete_Returns200_Deactivates_KeepsTheRow_AndAudits()
    {
        var h = CreateHarness();

        var response = await h.Client.DeleteAsync("/api/v1/employees/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Employee deactivated successfully.", root.GetProperty("message").GetString());
        Assert.False(root.GetProperty("data").GetProperty("isActive").GetBoolean());
        Assert.False(h.Employees.Stored(1).IsActive);
        Assert.Equal("Ravi Kumar", h.Employees.Stored(1).EmployeeName);
        Assert.Equal(3, h.Employees.Count);
        Assert.Equal("EmployeeDeactivated", Assert.Single(h.Audit.Entries).Action);

        var fetched = await h.Client.GetAsync("/api/v1/employees/1");
        Assert.False((await Root(fetched)).GetProperty("data").GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Delete_Returns404_ForUnknown_And409_ForAlreadyInactive()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.DeleteAsync("/api/v1/employees/999")).StatusCode);
        var again = await h.Client.DeleteAsync("/api/v1/employees/3");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already inactive", await again.Content.ReadAsStringAsync());
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ authorization

    [Fact]
    public async Task Employees_Endpoints_Return401_WithoutAToken_AndChangeNothing()
    {
        var h = CreateHarness(authenticate: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/employees/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PostAsync("/api/v1/employees", Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PutAsync("/api/v1/employees/1", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.DeleteAsync("/api/v1/employees/1")).StatusCode);
        Assert.Equal(3, h.Employees.Count);
        Assert.True(h.Employees.Stored(1).IsActive);
        Assert.Empty(h.Authorization.Checks);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Employees_Endpoints_Return403_WithoutThePermission_AndChangeNothing()
    {
        var h = CreateHarness(permissionGranted: false);

        var post = await h.Client.PostAsync("/api/v1/employees", Json(CreateBody()));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/employees/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync("/api/v1/employees/1", Json(UpdateBody(Rv(h, 1))))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync("/api/v1/employees/1")).StatusCode);

        Assert.Equal("You do not have permission to perform this action.", (await Root(post)).GetProperty("message").GetString());
        Assert.Equal(3, h.Employees.Count);
        Assert.Equal("Ravi Kumar", h.Employees.Stored(1).EmployeeName);
        Assert.True(h.Employees.Stored(1).IsActive);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task EachEndpoint_AsksForTheMasterEmployeePermission_MatchingItsVerb()
    {
        var h = CreateHarness();

        await h.Client.GetAsync("/api/v1/employees");
        await h.Client.GetAsync("/api/v1/employees/1");
        await h.Client.PostAsync("/api/v1/employees", Json(CreateBody()));
        await h.Client.PutAsync("/api/v1/employees/1", Json(UpdateBody(Rv(h, 1))));
        await h.Client.DeleteAsync("/api/v1/employees/1");

        Assert.Equal(
            new[] { PermissionAction.View, PermissionAction.View, PermissionAction.Add, PermissionAction.Edit, PermissionAction.Delete },
            h.Authorization.Checks.Select(c => c.Action));
        Assert.All(h.Authorization.Checks, c =>
        {
            Assert.Equal(ModuleCodes.MasterEmployee, c.ModuleCode);
            Assert.Equal("ADMIN", c.RoleCode);
        });
    }

    [Fact]
    public async Task PermissionsAreDecidedPerAction_FromTheMatrix_NotTheRoleName()
    {
        // e.g. MAINT_MANAGER in the seed: View/Add/Edit but no Delete on MASTER_EMPLOYEE. Even the ADMIN token is refused
        // Delete here, because the (stubbed) permission matrix says so.
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.MasterEmployee && action != PermissionAction.Delete);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync("/api/v1/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await h.Client.PostAsync("/api/v1/employees", Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsync("/api/v1/employees/1", Json(UpdateBody(Rv(h, 1))))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync("/api/v1/employees/1")).StatusCode);
        Assert.True(h.Employees.Stored(1).IsActive);
    }

    [Fact]
    public async Task ADepartmentPermission_DoesNotGrantEmployeeAccess()
    {
        var h = CreateHarness(decide: (_, module, _) => module == ModuleCodes.MasterDepartment);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/employees")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PostAsync("/api/v1/employees", Json(CreateBody()))).StatusCode);
        Assert.Equal(3, h.Employees.Count);
    }
}
