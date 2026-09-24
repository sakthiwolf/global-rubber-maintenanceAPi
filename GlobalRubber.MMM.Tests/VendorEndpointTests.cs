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
/// /api/v1/vendors through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model binding) with
/// the REAL VendorService. Only persistence is faked. Acting user: id 1 "Sakthi". Vendors: 1 Chennai Hydraulics,
/// 2 Precision Mold Tech (active), 3 Old Supplier (inactive).
/// </summary>
public class VendorEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly ApiWebApplicationFactory _factory;

    public VendorEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemoryVendorRepository Vendors, RecordingAuditLog Audit, StubPermissionAuthorization Authorization);

    private H CreateHarness(bool permissionGranted = true, bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null)
    {
        var roles = new SimpleRoleRepository(UserTestData.Roles());
        var users = new InMemoryUserRepository(UserTestData.Users(), roles.Find);
        var vendors = new InMemoryVendorRepository(VendorTestData.Vendors());
        var audit = new RecordingAuditLog();
        var authorization = decide is null ? new StubPermissionAuthorization(permissionGranted) : new StubPermissionAuthorization(decide);

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
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

        return new H(client, vendors, audit, authorization);
    }

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");
    private static async Task<JsonElement> Root(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    private static string Rv(H h, int id) => Convert.ToBase64String(h.Vendors.Stored(id).RowVersion);

    private static string CreateBody(string name = "SKF Bearings Distributor") =>
        JsonSerializer.Serialize(new { vendorName = name, category = "Spares", contactPerson = "Vignesh", mobile = "9884411003", email = "sales@skf.example", address = "Guindy" });

    private static string UpdateBody(string rowVersion, string name = "Precision Molds") =>
        JsonSerializer.Serialize(new { vendorName = name, category = "Mold Repair", contactPerson = "Elango", mobile = (string?)null, email = (string?)null, address = (string?)null, rowVersion });

    // ================================================================ GET

    [Fact]
    public async Task GetAll_Returns200_WithThePagedEnvelope()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync("/api/v1/vendors?pageNumber=1&pageSize=10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Vendors retrieved successfully.", root.GetProperty("message").GetString());
        var data = root.GetProperty("data");
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        var first = data.GetProperty("items")[0];
        Assert.Equal("VND-0001", first.GetProperty("vendorCode").GetString());
        Assert.Equal("Mohan", first.GetProperty("contactPerson").GetString());
        Assert.False(string.IsNullOrEmpty(first.GetProperty("rowVersion").GetString()));
    }

    [Fact]
    public async Task GetAll_HonoursTheSearchAndIsActiveFilters()
    {
        var h = CreateHarness();

        var search = (await Root(await h.Client.GetAsync("/api/v1/vendors?search=mold"))).GetProperty("data");
        var inactive = (await Root(await h.Client.GetAsync("/api/v1/vendors?isActive=false"))).GetProperty("data");

        Assert.Equal(1, search.GetProperty("totalCount").GetInt32());
        Assert.Equal("Precision Mold Tech", search.GetProperty("items")[0].GetProperty("vendorName").GetString());
        Assert.Equal("Old Supplier", inactive.GetProperty("items")[0].GetProperty("vendorName").GetString());
    }

    [Fact]
    public async Task GetById_Returns200_WithAllFields_And404_ForUnknown()
    {
        var h = CreateHarness();

        var data = (await Root(await h.Client.GetAsync("/api/v1/vendors/1"))).GetProperty("data");
        Assert.Equal("Chennai Hydraulics", data.GetProperty("vendorName").GetString());
        Assert.Equal("Spares", data.GetProperty("category").GetString());
        Assert.Equal("12 Industrial Estate, Chennai", data.GetProperty("address").GetString());
        Assert.Equal(Rv(h, 1), data.GetProperty("rowVersion").GetString());

        var missing = await h.Client.GetAsync("/api/v1/vendors/999");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.False((await Root(missing)).GetProperty("success").GetBoolean());
    }

    // ================================================================ POST

    [Fact]
    public async Task Post_Returns201_WithTheVendor_AndALocationHeader_AndAudits()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/vendors", Json(CreateBody()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("VND-0004", data.GetProperty("vendorCode").GetString());
        Assert.True(data.GetProperty("isActive").GetBoolean());
        Assert.Equal(4, h.Vendors.Count);

        var entry = Assert.Single(h.Audit.Entries);
        Assert.Equal("VendorCreated", entry.Action);
        Assert.Equal("Sakthi", entry.UserName);
        Assert.Equal("VND-0004", entry.RecordRef);
    }

    [Fact]
    public async Task Post_IgnoresClientSuppliedServerControlledFields()
    {
        var h = CreateHarness();

        var response = await h.Client.PostAsync("/api/v1/vendors", Json(
            """{"vendorName":"Stores Co","vendorId":99,"vendorCode":"HACK-9","isActive":false,"createdBy":42,"createdAt":"2000-01-01T00:00:00Z","rowVersion":"AAAA","updatedBy":7}"""));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var stored = h.Vendors.Stored(4);
        Assert.Equal("VND-0004", stored.VendorCode);
        Assert.True(stored.IsActive);
        Assert.Equal(1, stored.CreatedBy);
        Assert.NotEqual(new DateTime(2000, 1, 1), stored.CreatedAt.Date);
        Assert.Null(stored.UpdatedBy);
    }

    [Fact]
    public async Task Post_Returns400_ForInvalidFields_And409_ForADuplicateActiveName_WritingNothing()
    {
        var h = CreateHarness();

        var invalid = await h.Client.PostAsync("/api/v1/vendors", Json(JsonSerializer.Serialize(new { vendorName = " ", email = new string('x', 151) })));
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var errors = (await Root(invalid)).GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("VendorName is required.", errors);
        Assert.Contains("Email must be at most 150 characters.", errors);

        var duplicate = await h.Client.PostAsync("/api/v1/vendors", Json(CreateBody("CHENNAI HYDRAULICS")));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var text = await duplicate.Content.ReadAsStringAsync();
        Assert.Contains("already exists", text);
        Assert.DoesNotContain("SqlException", text);

        Assert.Equal(3, h.Vendors.Count);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ PUT

    [Fact]
    public async Task Put_Returns200_WithTheUpdatedVendor_AndAFreshRowVersion()
    {
        var h = CreateHarness();
        var oldVersion = Rv(h, 2);

        var response = await h.Client.PutAsync("/api/v1/vendors/2", Json(UpdateBody(oldVersion)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var data = (await Root(response)).GetProperty("data");
        Assert.Equal("Precision Molds", data.GetProperty("vendorName").GetString());
        Assert.Equal("Elango", data.GetProperty("contactPerson").GetString());
        Assert.Equal("VND-0002", data.GetProperty("vendorCode").GetString());
        Assert.NotEqual(oldVersion, data.GetProperty("rowVersion").GetString());
        Assert.Equal(1, h.Vendors.Stored(2).UpdatedBy);
        Assert.Equal("VendorUpdated", Assert.Single(h.Audit.Entries).Action);
    }

    [Fact]
    public async Task Put_IgnoresACodeOrStatusInTheBody()
    {
        var h = CreateHarness();

        var response = await h.Client.PutAsync("/api/v1/vendors/2", Json(
            $$"""{"vendorName":"Precision Mold Tech","vendorCode":"HACK-9","isActive":false,"createdBy":42,"rowVersion":"{{Rv(h, 2)}}"}"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("VND-0002", h.Vendors.Stored(2).VendorCode);
        Assert.True(h.Vendors.Stored(2).IsActive);
        Assert.Equal(1, h.Vendors.Stored(2).CreatedBy);
    }

    [Fact]
    public async Task Put_Returns409_ForAStaleRowVersion_WithACleanMessage_AndOverwritesNothing()
    {
        var h = CreateHarness();
        var stale = Rv(h, 2);
        h.Vendors.SimulateConcurrentModification(2);

        var response = await h.Client.PutAsync("/api/v1/vendors/2", Json(UpdateBody(stale)));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.Contains("modified by another user", text);
        Assert.DoesNotContain("DbUpdateConcurrencyException", text);
        Assert.Equal("Precision Mold Tech", h.Vendors.Stored(2).VendorName);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Put_Returns400_ForMissingRowVersion_404_ForUnknown_409_ForADuplicateName()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.BadRequest, (await h.Client.PutAsync("/api/v1/vendors/2", Json("""{"vendorName":"A"}"""))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.PutAsync("/api/v1/vendors/999", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await h.Client.PutAsync("/api/v1/vendors/2", Json(UpdateBody(Rv(h, 2), name: "chennai hydraulics")))).StatusCode);

        Assert.Equal("Precision Mold Tech", h.Vendors.Stored(2).VendorName);
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ DELETE (soft deactivation)

    [Fact]
    public async Task Delete_Returns200_Deactivates_KeepsTheRow_AndAudits()
    {
        var h = CreateHarness();

        var response = await h.Client.DeleteAsync("/api/v1/vendors/1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var root = await Root(response);
        Assert.Equal("Vendor deactivated successfully.", root.GetProperty("message").GetString());
        Assert.False(root.GetProperty("data").GetProperty("isActive").GetBoolean());
        Assert.False(h.Vendors.Stored(1).IsActive);
        Assert.Equal("Chennai Hydraulics", h.Vendors.Stored(1).VendorName);
        Assert.Equal(3, h.Vendors.Count);
        Assert.Equal("VendorDeactivated", Assert.Single(h.Audit.Entries).Action);

        var fetched = await h.Client.GetAsync("/api/v1/vendors/1");
        Assert.False((await Root(fetched)).GetProperty("data").GetProperty("isActive").GetBoolean());
    }

    [Fact]
    public async Task Delete_Returns404_ForUnknown_And409_ForAlreadyInactive()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.DeleteAsync("/api/v1/vendors/999")).StatusCode);
        var again = await h.Client.DeleteAsync("/api/v1/vendors/3");
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already inactive", await again.Content.ReadAsStringAsync());
        Assert.Empty(h.Audit.Entries);
    }

    // ================================================================ authorization

    [Fact]
    public async Task Vendors_Endpoints_Return401_WithoutAToken_AndChangeNothing()
    {
        var h = CreateHarness(authenticate: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/vendors")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync("/api/v1/vendors/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PostAsync("/api/v1/vendors", Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.PutAsync("/api/v1/vendors/2", Json(UpdateBody("AQ==")))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.DeleteAsync("/api/v1/vendors/2")).StatusCode);
        Assert.Equal(3, h.Vendors.Count);
        Assert.True(h.Vendors.Stored(2).IsActive);
        Assert.Empty(h.Authorization.Checks);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task Vendors_Endpoints_Return403_WithoutThePermission_AndChangeNothing()
    {
        var h = CreateHarness(permissionGranted: false);

        var post = await h.Client.PostAsync("/api/v1/vendors", Json(CreateBody()));
        Assert.Equal(HttpStatusCode.Forbidden, post.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/vendors")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/vendors/1")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PutAsync("/api/v1/vendors/2", Json(UpdateBody(Rv(h, 2))))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync("/api/v1/vendors/2")).StatusCode);

        Assert.Equal("You do not have permission to perform this action.", (await Root(post)).GetProperty("message").GetString());
        Assert.Equal(3, h.Vendors.Count);
        Assert.Equal("Precision Mold Tech", h.Vendors.Stored(2).VendorName);
        Assert.True(h.Vendors.Stored(2).IsActive);
        Assert.Empty(h.Audit.Entries);
    }

    [Fact]
    public async Task EachEndpoint_AsksForTheMasterVendorPermission_MatchingItsVerb()
    {
        var h = CreateHarness();

        await h.Client.GetAsync("/api/v1/vendors");
        await h.Client.GetAsync("/api/v1/vendors/1");
        await h.Client.PostAsync("/api/v1/vendors", Json(CreateBody()));
        await h.Client.PutAsync("/api/v1/vendors/2", Json(UpdateBody(Rv(h, 2))));
        await h.Client.DeleteAsync("/api/v1/vendors/2");

        Assert.Equal(
            new[] { PermissionAction.View, PermissionAction.View, PermissionAction.Add, PermissionAction.Edit, PermissionAction.Delete },
            h.Authorization.Checks.Select(c => c.Action));
        Assert.All(h.Authorization.Checks, c =>
        {
            Assert.Equal(ModuleCodes.MasterVendor, c.ModuleCode);
            Assert.Equal("ADMIN", c.RoleCode);
        });
    }

    [Fact]
    public async Task PermissionsAreDecidedPerAction_FromTheMatrix_NotTheRoleName()
    {
        // e.g. MAINT_MANAGER in the seed: View/Add/Edit but no Delete on MASTER_VENDOR. Even the ADMIN token is refused
        // Delete here, because the (stubbed) permission matrix says so.
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.MasterVendor && action != PermissionAction.Delete);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync("/api/v1/vendors")).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await h.Client.PostAsync("/api/v1/vendors", Json(CreateBody()))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await h.Client.PutAsync("/api/v1/vendors/2", Json(UpdateBody(Rv(h, 2))))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.DeleteAsync("/api/v1/vendors/2")).StatusCode);
        Assert.True(h.Vendors.Stored(2).IsActive);
    }

    [Fact]
    public async Task AnotherMastersPermission_DoesNotGrantVendorAccess()
    {
        var h = CreateHarness(decide: (_, module, _) => module == ModuleCodes.MasterEmployee);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync("/api/v1/vendors")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.PostAsync("/api/v1/vendors", Json(CreateBody()))).StatusCode);
        Assert.Equal(3, h.Vendors.Count);
    }
}
