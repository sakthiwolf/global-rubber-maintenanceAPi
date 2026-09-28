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
/// /api/v1/reports/molds through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model binding)
/// with the REAL MoldReportService and MoldReportQueryBuilder (run in memory over MoldReportScenario). Plant "today" is
/// 2026-09-28.
/// </summary>
public class MoldReportEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string Url = "/api/v1/reports/molds";
    private readonly ApiWebApplicationFactory _factory;

    public MoldReportEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemoryMoldReportRepository Repository, StubPermissionAuthorization Authorization);

    /// <summary>Real UtcNow (so the minted JWT is valid), fixed plant "today".</summary>
    private sealed class FixedPlantDateClock : IDateTimeProvider
    {
        public FixedPlantDateClock(DateOnly today) => Today = today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateOnly Today { get; }
    }

    private H CreateHarness(bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null)
    {
        var repository = new InMemoryMoldReportRepository(new MoldReportScenario());
        var authorization = new StubPermissionAuthorization(decide ?? ((_, _, _) => true));

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMoldReportRepository>();
                services.AddSingleton<IMoldReportRepository>(repository);
                services.RemoveAll<IDateTimeProvider>();
                services.AddSingleton<IDateTimeProvider>(new FixedPlantDateClock(MoldReportScenario.Today));
                services.RemoveAll<IPermissionAuthorizationService>();
                services.AddSingleton<IPermissionAuthorizationService>(authorization);
            });
        });

        var client = factory.CreateClient();
        if (authenticate)
        {
            TestAuth.Authenticate(client, factory, "MAINT_MANAGER", 1);
        }

        return new H(client, repository, authorization);
    }

    private static async Task<JsonElement> Root(HttpResponseMessage r) =>
        JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();

    private static async Task<JsonElement> Data(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var root = await Root(r);
        Assert.True(root.GetProperty("success").GetBoolean());
        return root.GetProperty("data");
    }

    private static List<string> Col(JsonElement page, string property) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty(property).GetString()!).ToList();

    private static JsonElement Row(JsonElement page, string property, string value) =>
        page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty(property).GetString() == value);

    private static async Task<string[]> CsvLines(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("text/csv", r.Content.Headers.ContentType?.MediaType);
        var bytes = await r.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "UTF-8 BOM expected");
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    private static async Task AssertBadRequest(HttpResponseMessage response, string error)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await Root(response);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains(error, root.GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
    }

    // ================================================================ Mold List

    [Fact]
    public async Task MoldList_EveryMold_InCodeOrder_WithLifeAndPmFiguresFromTheExistingRules()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url));

        Assert.Equal(new[] { "MLD-0001", "MLD-0002", "MLD-0003", "MLD-0004" }, Col(data, "moldCode"));
        var m1 = Row(data, "moldCode", "MLD-0001");
        Assert.Equal("PRD-0001", m1.GetProperty("productCode").GetString());
        Assert.Equal("Warning", m1.GetProperty("lifeState").GetString());
        Assert.Equal("2026-09-06", m1.GetProperty("lastMaintenanceDate").GetString());
        Assert.Equal(451_000, m1.GetProperty("lastMaintenanceUsage").GetInt32());
        Assert.Equal(500_000, m1.GetProperty("pmNextThresholdShots").GetInt64());
        Assert.Equal(40_000, m1.GetProperty("pmRemainingShots").GetInt64());
        Assert.Equal("Normal", m1.GetProperty("pmState").GetString()); // the open Cleaning PM is not a shot-based PM
        Assert.Equal("In Maintenance", Row(data, "moldCode", "MLD-0002").GetProperty("pmState").GetString());
        Assert.Equal("Overdue", Row(data, "moldCode", "MLD-0003").GetProperty("pmState").GetString());
        var m4 = Row(data, "moldCode", "MLD-0004");
        Assert.Equal("Not Configured", m4.GetProperty("pmState").GetString());
        Assert.False(m4.GetProperty("isActive").GetBoolean());
        Assert.Equal(JsonValueKind.Null, m4.GetProperty("pmNextThresholdShots").ValueKind);
        Assert.False(m1.TryGetProperty("machineCode", out _)); // molds have no machine
    }

    [Theory]
    [InlineData("?productId=2", new[] { "MLD-0003", "MLD-0004" })]
    [InlineData("?isActive=false", new[] { "MLD-0004" })]
    [InlineData("?isActive=true", new[] { "MLD-0001", "MLD-0002", "MLD-0003" })]
    [InlineData("?lifeState=replace", new[] { "MLD-0002" })]
    [InlineData("?status=Replacement%20Due", new[] { "MLD-0002" })]
    [InlineData("?moldType=Blow", new[] { "MLD-0003", "MLD-0004" })]
    [InlineData("?moldId=2", new[] { "MLD-0002" })]
    [InlineData("?search=BAY", new[] { "MLD-0001" })]
    [InlineData("?search=bottle", new[] { "MLD-0003", "MLD-0004" })]
    [InlineData("?search=nothing", new string[0])]
    public async Task MoldList_Filters_AreAppliedByTheServer(string queryString, string[] expected)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + queryString));

        Assert.Equal(expected, Col(data, "moldCode"));
        Assert.Equal(expected.Length, data.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task MoldList_IsPagedByTheServer()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "?pageNumber=2&pageSize=3"));

        Assert.Equal(new[] { "MLD-0004" }, Col(data, "moldCode"));
        Assert.Equal(4, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, data.GetProperty("totalPages").GetInt32());
        Assert.Equal(new ReportPage(2, 3), Assert.Single(h.Repository.Calls).Page);
    }

    [Theory]
    [InlineData("?status=Broken", "Status must be one of: Available, In Production, Maintenance, Replacement Due, Retired.")]
    [InlineData("?lifeState=Expired", "LifeState must be one of: Normal, Warning, Replace.")]
    public async Task MoldList_InvalidFilter_Is400(string queryString, string error)
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + queryString), error);
        Assert.Empty(h.Repository.Calls);
    }

    // ================================================================ Usage

    [Fact]
    public async Task Usage_NoRange_EveryMold_WithAllTimeSavedProduction_AndTheMasterLifeRules()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/usage"));

        Assert.Equal(new[] { "MLD-0001", "MLD-0002", "MLD-0003", "MLD-0004" }, Col(data, "moldCode"));
        var m1 = Row(data, "moldCode", "MLD-0001");
        Assert.Equal(1_500, m1.GetProperty("rangeShots").GetInt64());
        Assert.Equal(2, m1.GetProperty("rangeEntryCount").GetInt32());
        Assert.Equal("MAC-0002", m1.GetProperty("lastMachineCode").GetString());
        Assert.Equal("2026-09-10", m1.GetProperty("lastProductionDate").GetString());
        Assert.Equal(460_000, m1.GetProperty("currentUsageShots").GetInt32());
        Assert.Equal(40_000, m1.GetProperty("remainingShots").GetInt32());
        Assert.Equal(92, m1.GetProperty("lifeUsedPercent").GetInt32());
        // The cancelled 700-piece entry is not counted.
        Assert.Equal(2_000, Row(data, "moldCode", "MLD-0003").GetProperty("rangeShots").GetInt64());
        Assert.Equal(0, Row(data, "moldCode", "MLD-0002").GetProperty("rangeShots").GetInt64());
    }

    [Theory]
    [InlineData("?fromDate=2026-09-01&toDate=2026-09-09", new[] { "MLD-0001" })]
    [InlineData("?fromDate=2026-09-10", new[] { "MLD-0001", "MLD-0003" })]
    [InlineData("?machineId=2", new[] { "MLD-0001" })]
    [InlineData("?lifeState=Warning", new[] { "MLD-0001" })]
    [InlineData("?search=cap", new[] { "MLD-0001", "MLD-0002" })]
    [InlineData("?sortBy=rangeShots", new[] { "MLD-0003", "MLD-0001", "MLD-0002", "MLD-0004" })]
    [InlineData("?sortBy=usage", new[] { "MLD-0002", "MLD-0001", "MLD-0004", "MLD-0003" })]
    [InlineData("?sortBy=lifeUsed&sortDirection=asc", new[] { "MLD-0003", "MLD-0004", "MLD-0001", "MLD-0002" })]
    [InlineData("?sortBy=code&sortDirection=desc", new[] { "MLD-0004", "MLD-0003", "MLD-0002", "MLD-0001" })]
    [InlineData("?fromDate=2030-01-01", new string[0])]
    public async Task Usage_FiltersAndSorting_AreAppliedByTheServer(string queryString, string[] expected)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/usage" + queryString));

        Assert.Equal(expected, Col(data, "moldCode"));
    }

    [Fact]
    public async Task Usage_RangeAndMachine_RestrictTheProductionFigures()
    {
        var h = CreateHarness();

        var inRange = Row(await Data(await h.Client.GetAsync(Url + "/usage?fromDate=2026-09-01&toDate=2026-09-09")), "moldCode", "MLD-0001");
        var onMachine = Row(await Data(await h.Client.GetAsync(Url + "/usage?machineId=2")), "moldCode", "MLD-0001");

        Assert.Equal(1_000, inRange.GetProperty("rangeShots").GetInt64());
        Assert.Equal("MAC-0001", inRange.GetProperty("lastMachineCode").GetString());
        Assert.Equal(500, onMachine.GetProperty("rangeShots").GetInt64());
        Assert.Equal(1, onMachine.GetProperty("rangeEntryCount").GetInt32());
    }

    [Theory]
    [InlineData("?fromDate=2026-09-10&toDate=2026-09-01", "FromDate must be on or before ToDate.")]
    [InlineData("?sortBy=remaining", "SortBy must be one of: code, usage, rangeShots, lifeUsed.")]
    [InlineData("?sortDirection=up", "SortDirection must be one of: asc, desc.")]
    public async Task Usage_InvalidFilter_Is400(string queryString, string error)
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/usage" + queryString), error);
    }

    // ================================================================ Life Status

    [Fact]
    public async Task LifeStatus_Summary_CoversTheWholeFilteredSet_AndRowsAreMostUsedFirst()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/life-status?pageSize=1"));

        var s = data.GetProperty("summary");
        Assert.Equal(4, s.GetProperty("totalMolds").GetInt32());
        Assert.Equal(2, s.GetProperty("normalCount").GetInt32());
        Assert.Equal(1, s.GetProperty("warningCount").GetInt32());
        Assert.Equal(1, s.GetProperty("replaceCount").GetInt32());
        Assert.Equal(1, s.GetProperty("replacementRequiredCount").GetInt32());
        Assert.Equal(1, s.GetProperty("retiredCount").GetInt32());
        var page = data.GetProperty("page");
        Assert.Equal(new[] { "MLD-0002" }, Col(page, "moldCode"));
        Assert.Equal(4, page.GetProperty("totalCount").GetInt32());
        var m2 = page.GetProperty("items")[0];
        Assert.Equal(98, m2.GetProperty("lifeUsedPercent").GetInt32());
        Assert.Equal(10_000, m2.GetProperty("remainingShots").GetInt32());
        Assert.Equal("Replacement Required", m2.GetProperty("replacementStatus").GetString());
    }

    [Theory]
    [InlineData("", new[] { "MLD-0002", "MLD-0001", "MLD-0004", "MLD-0003" })]
    [InlineData("?sortBy=remaining&sortDirection=asc", new[] { "MLD-0002", "MLD-0001", "MLD-0004", "MLD-0003" })]
    [InlineData("?sortBy=remaining", new[] { "MLD-0003", "MLD-0004", "MLD-0001", "MLD-0002" })]
    [InlineData("?sortBy=code", new[] { "MLD-0001", "MLD-0002", "MLD-0003", "MLD-0004" })]
    [InlineData("?lifeState=Normal", new[] { "MLD-0004", "MLD-0003" })]
    [InlineData("?productId=1", new[] { "MLD-0002", "MLD-0001" })]
    [InlineData("?status=Retired", new[] { "MLD-0004" })]
    public async Task LifeStatus_FiltersAndSorting(string queryString, string[] expected)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/life-status" + queryString));

        Assert.Equal(expected, Col(data.GetProperty("page"), "moldCode"));
        Assert.Equal(expected.Length, data.GetProperty("summary").GetProperty("totalMolds").GetInt32());
    }

    [Fact]
    public async Task LifeStatus_RejectsTheReplacementOnlyFilter()
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/life-status?replacementStatus=Retired"),
            "ReplacementStatus applies to the replacement report only.");
    }

    // ================================================================ Replacement

    [Fact]
    public async Task Replacement_OnlyMoldsNeedingReplacementOrRetired_NothingClaimedAsReplaced()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/replacement"));

        var page = data.GetProperty("page");
        Assert.Equal(new[] { "MLD-0002", "MLD-0004" }, Col(page, "moldCode"));
        Assert.Equal("Replacement Required", Row(page, "moldCode", "MLD-0002").GetProperty("replacementStatus").GetString());
        Assert.Equal("Retired", Row(page, "moldCode", "MLD-0004").GetProperty("replacementStatus").GetString());
        Assert.False(Row(page, "moldCode", "MLD-0002").TryGetProperty("replacementDate", out _));
        var s = data.GetProperty("summary");
        Assert.Equal(2, s.GetProperty("totalMolds").GetInt32());
        Assert.Equal(1, s.GetProperty("replacementRequiredCount").GetInt32());
        Assert.Equal(1, s.GetProperty("retiredCount").GetInt32());
    }

    [Theory]
    [InlineData("?replacementStatus=replacement%20required", new[] { "MLD-0002" })]
    [InlineData("?replacementStatus=Retired", new[] { "MLD-0004" })]
    [InlineData("?search=cap", new[] { "MLD-0002" })]
    [InlineData("?productId=2", new[] { "MLD-0004" })]
    public async Task Replacement_Filters(string queryString, string[] expected)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/replacement" + queryString));

        Assert.Equal(expected, Col(data.GetProperty("page"), "moldCode"));
    }

    [Fact]
    public async Task Replacement_InvalidStatus_Is400()
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/replacement?replacementStatus=Replaced"),
            "ReplacementStatus must be one of: Replacement Required, Retired.");
    }

    // ================================================================ Maintenance

    [Fact]
    public async Task Maintenance_NewestFirst_WithTheMoldPmPagesStatuses()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/maintenance"));

        Assert.Equal(new[] { "MPMD-0003", "MPMD-0004", "MPMD-0002", "MPMD-0001" }, Col(data, "pmNo"));
        Assert.Equal(new[] { "Due", "In Progress", "Overdue", "Completed" }, Col(data, "status"));
        var pm1 = Row(data, "pmNo", "MPMD-0001");
        Assert.Equal(450_000, pm1.GetProperty("thresholdShots").GetInt32());
        Assert.Equal(50_000, pm1.GetProperty("intervalShots").GetInt32());
        Assert.Equal(450_100, pm1.GetProperty("usageAtTrigger").GetInt32());
        Assert.Equal(451_000, pm1.GetProperty("usageAtCompletion").GetInt32());
        Assert.Equal("Ravi", pm1.GetProperty("maintenanceBy").GetString());
        Assert.Equal("Shot-based", pm1.GetProperty("category").GetString());
    }

    [Theory]
    [InlineData("?status=due", new[] { "MPMD-0003" })]
    [InlineData("?status=OVERDUE", new[] { "MPMD-0002" })]
    [InlineData("?status=in-progress", new[] { "MPMD-0004" })]
    [InlineData("?status=completed", new[] { "MPMD-0001" })]
    [InlineData("?category=cleaning", new[] { "MPMD-0003" })]
    [InlineData("?moldId=1", new[] { "MPMD-0003", "MPMD-0001" })]
    [InlineData("?search=ravi", new[] { "MPMD-0001" })]
    [InlineData("?search=bottle", new[] { "MPMD-0002" })]
    [InlineData("?fromDate=2026-09-20&toDate=2026-09-25", new[] { "MPMD-0004", "MPMD-0002" })]
    [InlineData("?fromDate=2030-01-01", new string[0])]
    public async Task Maintenance_Filters(string queryString, string[] expected)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/maintenance" + queryString));

        Assert.Equal(expected, Col(data, "pmNo"));
        Assert.Equal(expected.Length, data.GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("?status=Scheduled", "Status must be one of: due, overdue, in-progress, completed.")]
    [InlineData("?category=Repair", "Category must be one of: Scheduled, Shot-based, Damage Repair, Cleaning, Inspection, Preventive, Replacement.")]
    [InlineData("?fromDate=2026-09-25&toDate=2026-09-20", "FromDate must be on or before ToDate.")]
    public async Task Maintenance_InvalidFilter_Is400(string queryString, string error)
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/maintenance" + queryString), error);
    }

    // ================================================================ Lookups

    [Fact]
    public async Task Lookups_ReturnFilterOptions()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/lookups"));

        Assert.Equal(4, data.GetProperty("molds").GetArrayLength());
        Assert.Equal(new[] { "PRD-0001", "PRD-0002" }, data.GetProperty("products").EnumerateArray().Select(p => p.GetProperty("code").GetString()));
        Assert.Equal(2, data.GetProperty("machines").GetArrayLength());
        Assert.Equal(new[] { "Blow", "Injection" }, data.GetProperty("moldTypes").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(MoldLifeState.All, data.GetProperty("lifeStates").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(MoldPmBucket.All, data.GetProperty("maintenanceStatuses").EnumerateArray().Select(t => t.GetString()));
        Assert.Contains(("MAINT_MANAGER", ModuleCodes.RptMold, PermissionAction.View), h.Authorization.Checks);
    }

    // ================================================================ Export

    [Fact]
    public async Task ExportMoldList_IsTheWholeFilteredSet_WithReadableHeaders()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync(Url + "/export?productId=2&pageSize=1");
        var lines = await CsvLines(response);

        Assert.Equal("mold-list-report-20260928.csv", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.StartsWith("Mold Code,Mold Name,Mold Type,Cavities,Product,Location,Current Usage (shots),Maximum Shots,Warning Shots,Replacement Shots,Life State,Mold Status,Active Status", lines[0]);
        Assert.Equal(3, lines.Length); // header + MLD-0003 + MLD-0004, paging ignored
        Assert.Contains("Inactive (Retired)", lines[2]);
        Assert.Null(Assert.Single(h.Repository.Calls).Page);
    }

    [Fact]
    public async Task ExportUsage_FollowsTheSortAndRange()
    {
        var h = CreateHarness();

        var lines = await CsvLines(await h.Client.GetAsync(Url + "/usage/export?sortBy=rangeShots"));

        Assert.StartsWith("Mold Code,Mold Name,Product,Production Shots in Range,Production Entries in Range,Last Production Date,Last Machine", lines[0]);
        Assert.Equal(new[] { "MLD-0003", "MLD-0001", "MLD-0002", "MLD-0004" }, lines.Skip(1).Select(l => l.Split(',')[0]));
        Assert.StartsWith("MLD-0003,Bottle Mold,PRD-0002 - Bottle,2000,1,2026-09-15,MAC-0001 - Injection Moulding M/c 1,", lines[1]);
    }

    [Fact]
    public async Task ExportLifeStatus_AndReplacement()
    {
        var h = CreateHarness();

        var life = await CsvLines(await h.Client.GetAsync(Url + "/life-status/export?lifeState=Replace"));
        var repl = await CsvLines(await h.Client.GetAsync(Url + "/replacement/export"));

        Assert.Equal(2, life.Length);
        Assert.DoesNotContain("Replacement Status", life[0]);
        Assert.EndsWith(",Replacement Status", repl[0]);
        Assert.Equal(3, repl.Length);
        Assert.EndsWith(",Replacement Due,Replacement Required", repl[1]);
        Assert.EndsWith(",Retired,Retired", repl[2]);
    }

    [Fact]
    public async Task ExportMaintenance_RespectsTheStatus()
    {
        var h = CreateHarness();

        var lines = await CsvLines(await h.Client.GetAsync(Url + "/maintenance/export?status=completed"));

        Assert.Equal("PM No,Mold Code,Mold Name,Category,Due Date,Completed Date,Status,Threshold (shots),Interval (shots),Usage at Trigger,Usage at Completion,Maintenance By,Remarks", lines[0]);
        Assert.Equal("MPMD-0001,MLD-0001,Cap Mold A,Shot-based,2026-09-05,2026-09-06,Completed,450000,50000,450100,451000,Ravi,", lines[1]);
        Assert.Equal(2, lines.Length);
    }

    [Fact]
    public async Task Export_InvalidFilter_Is400_InTheApiResponseShape()
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/usage/export?fromDate=2026-09-10&toDate=2026-09-01"), "FromDate must be on or before ToDate.");
    }

    // ================================================================ Authorization

    public static readonly TheoryData<string> ReportPaths = new() { "", "/usage", "/life-status", "/maintenance", "/replacement", "/lookups" };
    public static readonly TheoryData<string> ExportPaths = new() { "/export", "/usage/export", "/life-status/export", "/maintenance/export", "/replacement/export" };

    [Theory]
    [MemberData(nameof(ReportPaths))]
    [MemberData(nameof(ExportPaths))]
    public async Task EveryEndpoint_Without_A_Token_Is401(string path)
    {
        var h = CreateHarness(authenticate: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync(Url + path)).StatusCode);
        Assert.Empty(h.Repository.Calls);
    }

    [Theory]
    [MemberData(nameof(ReportPaths))]
    public async Task Reports_RequireRptMoldView(string path)
    {
        var h = CreateHarness(decide: (_, module, action) => !(module == ModuleCodes.RptMold && action == PermissionAction.View));

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync(Url + path)).StatusCode);
        Assert.Equal(("MAINT_MANAGER", ModuleCodes.RptMold, PermissionAction.View), Assert.Single(h.Authorization.Checks));
        Assert.Empty(h.Repository.Calls);
    }

    [Theory]
    [MemberData(nameof(ExportPaths))]
    public async Task Exports_RequireRptMoldExport_ViewAloneIsNotEnough(string path)
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.RptMold && action == PermissionAction.View);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync(Url + path)).StatusCode);
        Assert.Equal(("MAINT_MANAGER", ModuleCodes.RptMold, PermissionAction.Export), Assert.Single(h.Authorization.Checks));
        Assert.Empty(h.Repository.Calls);
    }

    [Fact]
    public async Task ReportEndpoints_AskOnlyForTheMoldReportModule()
    {
        // Only RPT_MOLD View: every tab readable, no MASTER_MOLD / TRN_* / RPT_MACHINE permission needed.
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.RptMold && action == PermissionAction.View);

        foreach (var path in ReportPaths)
        {
            Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync(Url + (string)path[0])).StatusCode);
        }

        Assert.All(h.Authorization.Checks, c => Assert.Equal(ModuleCodes.RptMold, c.ModuleCode));
    }

    [Fact]
    public void TheReportRepository_IsReadOnly()
    {
        var methods = typeof(IMoldReportRepository).GetMethods().Select(m => m.Name).ToList();

        Assert.NotEmpty(methods);
        Assert.All(methods, name => Assert.StartsWith("Get", name));
    }
}
