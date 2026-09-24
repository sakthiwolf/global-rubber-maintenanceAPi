using System.Net;
using System.Text;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// /api/v1/departments through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model
/// binding) with the REAL DepartmentService. Only persistence is faked (in-memory) because the real repository
/// needs a live SQL Server. Acting user: id 1 "Sakthi". Departments: 1 Injection Moulding, 2 Mixing, 3 Old Dept (inactive).
/// </summary>
public class DepartmentEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public DepartmentEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(
        HttpClient Client,
        InMemoryDepartmentRepository Departments,
        RecordingAuditLog Audit,
        StubPermissionAuthorization Authorization);

    private H CreateHarness(bool permissionGranted = true, bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var departments = new InMemoryDepartmentRepository(DepartmentTestData.Departments());
        var audit = new RecordingAuditLog();
        var authorization = decide is null ? new StubPermissionAuthorization(permissionGranted) : new StubPermissionAuthorization(decide);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
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

        return new H(client, departments, audit, authorization);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Root(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static string Rv(H h, int id) => Convert.ToBase64String(h.Departments.Stored(id).RowVersion);

    private static string CreateBody(string name = "Quality Control", string? remarks = "QC lab") =>
        JsonSerializer.Serialize(new { departmentName = name, remarks });

    private static string UpdateBody(string rowVersion, string name = "Mixing Dept", string? remarks = "Bay 2") =>
        JsonSerializer.Serialize(new { departmentName = name, remarks, rowVersion });

    // ================================================================ GET

    [Fact]
    public async Task GetAll_Returns200_WithThePagedEnvelope_AndDepartmentItems()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync("/api/v1/departments?pageNumber=1&pageSize=2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.True(root.GetProperty("success").GetBoolean());
        Assert.Equal("Departments retrieved successfully.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, data.GetProperty("pageSize").GetInt32());
        var items = data.GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal("DEP-0001", items[0].GetProperty("departmentCode").GetString());
        Assert.Equal("Injection Moulding", items[0].GetProperty("departmentName").GetString());
        Assert.True(items[0].GetProperty("isActive").GetBoolean());
        Assert.False(string.IsNullOrEmpty(items[0].GetProperty("rowVersion").GetString()));
    }

    [Fact]
    public async Task GetAll_HonoursTheSearchAndIsActiveFilters()
    {
        var h = CreateHarness();

        var search = (await Root(await h.Client.GetAsync("/api/v1/departments?search=mix"))).GetProperty("data");
        var inactive = (await Root(await h.Client.GetAsync("/api/v1/departments?isActive=false"))).GetProperty("data");
        var active = (await Root(await h.Client.GetAsync("/api/v1/departments?isActive=true"))).GetProperty("data");

        Assert.Equal("Mixing", search.GetProperty("items")[0].GetProperty("departmentName").GetString());
        Assert.Equal(1, search.GetProperty("totalCount").GetInt32());
        Assert.Equal("Old Dept", inactive.GetProperty("items")[0].GetProperty("departmentName").GetString());
        Assert.Equal(2, active.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task GetById_Returns200_WithTheFields_AndTheRowVersion()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync("/api/v1/departments/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal(1, data.GetProperty("departmentId").GetInt32());
        Assert.Equal("DEP-0001", data.GetProperty("departmentCode").GetString());
        Assert.Equal("Shop floor 1", data.GetProperty("remarks").GetString());
        Assert.Equal(Rv(h, 1), data.GetProperty("rowVersion").GetString());
    }

    [Fact]
    public async Task GetById_Returns404_ForAnUnknownDepartment()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync("/api/v1/departments/999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.False((await Root(response)).GetProperty("success").GetBoolean());
    }

    // ================================================================ POST

    [Fact]
    public async Task Post_Returns201_WithTheDepartment_AndALocationHeader()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/departments", Json(CreateBody()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var root = await Root(response);
        Assert.Equal("Department created successfully.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal("DEP-0004", data.GetProperty("departmentCode").GetString());
        Assert.Equal("Quality Control", data.GetProperty("departmentName").GetString());
        Assert.True(data.GetProperty("isActive").GetBoolean());
        Assert.Equal(4, h.Departments.Count);
    }

    [Fact]
    public async Task Post_IgnoresClientSuppliedServerControlledFields()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/departments", Json(
            """{"departmentName":"Stores","departmentId":99,"departmentCode":"HACK-9","isActive":false,"createdBy":42,"rowVersion":"AAAA","updatedBy":7}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = h.Departments.Stored(4);
        Assert.Equal("DEP-0004", stored.DepartmentCode);
        Assert.True(stored.IsActive);
        Assert.Equal(1, stored.CreatedBy); // the token's user, not 42
        Assert.Null(stored.UpdatedBy);
    }

    [Fact]
    public async Task Post_WritesExactlyOneDepartmentCreatedAudit_ForTheAuthenticatedUser()
    {
        var h = CreateHarness();

        await h.Client.PostAsync("/api/v1/departments", Json(CreateBody()));

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal("DepartmentCreated", entry.Action);
        Assert.Equal(1, entry.UserId);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Equal("DEP-0004", entry.RecordRef);
        Assert.Equal("Department", entry.EntityName);
    }

    [Fact]
    public async Task Post_Returns400_WithFieldErrors_ForInvalidInput_AndWritesNothing()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/departments", Json(CreateBody(name: " ", remarks: new string('r', 501))));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await Root(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("DepartmentName is required.", errors);
        Assert.Contains("Remarks must be at most 500 characters.", errors);
        Assert.Equal(3, h.Departments.Count);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Post_Returns409_ForADuplicateActiveName_AndNothingLeaks()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/departments", Json(CreateBody(name: "MIXING")));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("already exists", text);
        Assert.DoesNotContain("SqlException", text);
        Assert.DoesNotContain("UQ_", text);
        Assert.Equal(3, h.Departments.Count);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ PUT

    [Fact]
    public async Task Put_Returns200_WithTheUpdatedDepartment_AndAFreshRowVersion()
    {
        var h = CreateHarness();
        var oldVersion = Rv(h, 2);

        var response = await h.Client.PutAsync("/api/v1/departments/2", Json(UpdateBody(oldVersion)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("Mixing Dept", data.GetProperty("departmentName").GetString());
        Assert.Equal("Bay 2", data.GetProperty("remarks").GetString());
        Assert.Equal("DEP-0002", data.GetProperty("departmentCode").GetString());
        Assert.NotEqual(oldVersion, data.GetProperty("rowVersion").GetString());
        Assert.Equal(1, h.Departments.Stored(2).UpdatedBy);
        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal("DepartmentUpdated", entry.Action);
        Assert.Equal("DEP-0002", entry.RecordRef);
    }

    [Fact]
    public async Task Put_IgnoresACodeOrStatusInTheBody()
    {
        var h = CreateHarness();

        var response = await h.Client.PutAsync("/api/v1/departments/2", Json(
            $$"""{"departmentName":"Mixing","departmentCode":"HACK-9","isActive":false,"rowVersion":"{{Rv(h, 2)}}"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("DEP-0002", h.Departments.Stored(2).DepartmentCode);
        Assert.True(h.Departments.Stored(2).IsActive);
    }

    [Fact]
    public async Task Put_Returns409_ForAStaleRowVersion_WithACleanMessage_AndOverwritesNothing()
    {
        var h = CreateHarness();
        var staleVersion = Rv(h, 2);
        h.Departments.SimulateConcurrentModification(2);

        var response = await h.Client.PutAsync("/api/v1/departments/2", Json(UpdateBody(staleVersion)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("modified by another user", text);
        Assert.DoesNotContain("DbUpdateConcurrencyException", text);
        Assert.Equal("Mixing", h.Departments.Stored(2).DepartmentName);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns400_ForAMissingOrInvalidRowVersion()
    {
        var h = CreateHarness();

        var missing = await h.Client.PutAsync("/api/v1/departments/2", Json("""{"departmentName":"Mixing"}"""));
        var invalid = await h.Client.PutAsync("/api/v1/departments/2", Json(UpdateBody("not base64 !!")));

        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns404_ForAnUnknownDepartment_And409_ForAnotherActiveDepartmentsName()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PutAsync("/api/v1/departments/999", Json(UpdateBody("AQ==")))).StatusCode);

        var duplicate = await h.Client.PutAsync("/api/v1/departments/2", Json(UpdateBody(Rv(h, 2), name: "injection moulding")));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("Mixing", h.Departments.Stored(2).DepartmentName);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ DELETE (soft deactivation)

    [Fact]
    public async Task Delete_Returns200_WithTheDeactivatedDepartment_AndKeepsTheRow()
    {
        var h = CreateHarness();

        var response = await h.Client.DeleteAsync("/api/v1/departments/2");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Department deactivated successfully.", root.GetProperty("message").GetString());
        Assert.False(root.GetProperty("data").GetProperty("isActive").GetBoolean());
        Assert.False(h.Departments.Stored(2).IsActive);
        Assert.Equal("Mixing", h.Departments.Stored(2).DepartmentName);
        Assert.Equal(1, h.Departments.Stored(2).UpdatedBy);
        Assert.Equal(3, h.Departments.Count);

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal("DepartmentDeactivated", entry.Action);
        Assert.Equal("DEP-0002", entry.RecordRef);
    }

    [Fact]
    public async Task Delete_ThenGetById_StillReturnsTheDepartment_AsInactive()
    {
        var h = CreateHarness();

        await h.Client.DeleteAsync("/api/v1/departments/2");
        var fetched = await h.Client.GetAsync("/api/v1/departments/2");

        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.False((await Root(fetched)).GetProperty("data").GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Delete_Returns404_ForUnknown_And409_ForAlreadyInactive()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.DeleteAsync("/api/v1/departments/999")).StatusCode);

        var again = await h.Client.DeleteAsync("/api/v1/departments/3");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already inactive", await again.Content.ReadAsStringAsync());
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ authorization

    [Fact]
    public async Task Departments_Endpoints_Return401_WithoutAToken_AndChangeNothing()
    {
        var h = CreateHarness(authenticate: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/departments")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/departments/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PostAsync("/api/v1/departments", Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PutAsync("/api/v1/departments/2", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.DeleteAsync("/api/v1/departments/2")).StatusCode);
        Assert.Equal(3, h.Departments.Count);
        Assert.True(h.Departments.Stored(2).IsActive);
        Assert.Empty(h.Authorization.Checks);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Departments_Endpoints_Return403_WithoutThePermission_AndChangeNothing()
    {
        var h = CreateHarness(permissionGranted: false);

        var post = await h.Client.PostAsync("/api/v1/departments", Json(CreateBody()));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/departments")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/departments/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync("/api/v1/departments/2", Json(UpdateBody(Rv(h, 2))))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync("/api/v1/departments/2")).StatusCode);

        Assert.Equal("You do not have permission to perform this action.", (await Root(post)).GetProperty("message").GetString());
        Assert.Equal(3, h.Departments.Count);
        Assert.Equal("Mixing", h.Departments.Stored(2).DepartmentName);
        Assert.True(h.Departments.Stored(2).IsActive);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task EachEndpoint_AsksForTheMasterDepartmentPermission_MatchingItsVerb()
    {
        var h = CreateHarness();

        await h.Client.GetAsync("/api/v1/departments");
        await h.Client.GetAsync("/api/v1/departments/1");
        await h.Client.PostAsync("/api/v1/departments", Json(CreateBody()));
        await h.Client.PutAsync("/api/v1/departments/2", Json(UpdateBody(Rv(h, 2))));
        await h.Client.DeleteAsync("/api/v1/departments/2");

        Assert.Equal(
            new[] { PermissionAction.View, PermissionAction.View, PermissionAction.Add, PermissionAction.Edit, PermissionAction.Delete },
            h.Authorization.Checks.Select(c => c.Action));
        Assert.All(h.Authorization.Checks, c =>
        {
            Assert.Equal(ModuleCodes.MasterDepartment, c.ModuleCode);
            Assert.Equal("ADMIN", c.RoleCode);
        });
    }

    [Fact]
    public async Task APermissionIsCheckedPerAction_ViewOnlyCanReadButNotWrite()
    {
        // A role with only View (e.g. MAINT_ENGINEER in the seed): reads pass, every write is 403 - decided by
        // the permission matrix, never by the role name.
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.MasterDepartment && action == PermissionAction.View);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync("/api/v1/departments")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync("/api/v1/departments/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PostAsync("/api/v1/departments", Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync("/api/v1/departments/2", Json(UpdateBody(Rv(h, 2))))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync("/api/v1/departments/2")).StatusCode);
        Assert.Equal(3, h.Departments.Count);
        Assert.Empty(h.Audit.Entries);
    }
}
