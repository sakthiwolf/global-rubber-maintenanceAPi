using System.Net;
using System.Text;
using System.Text.Json;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Constants;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// /api/v1/reports/maintenance through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model
/// binding) with the REAL MaintenanceReportService and MaintenanceReportQueryBuilder (run in memory over
/// MaintenanceReportScenario). Plant "today" is 2026-09-28.
/// </summary>
public class MaintenanceReportEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string Url = "/api/v1/reports/maintenance";
    private readonly ApiWebApplicationFactory _factory;

    public MaintenanceReportEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemoryMaintenanceReportRepository Repository, StubPermissionAuthorization Authorization);

    /// <summary>Real UtcNow (so the minted JWT is valid), fixed plant "today".</summary>
    private sealed class FixedPlantDateClock : IDateTimeProvider
    {
        public FixedPlantDateClock(DateOnly today) => Today = today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateOnly Today { get; }
    }

    private H CreateHarness(bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null)
    {
        var repository = new InMemoryMaintenanceReportRepository(new MaintenanceReportScenario());
        var authorization = new StubPermissionAuthorization(decide ?? ((_, _, _) => true));

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMaintenanceReportRepository>();
                services.AddSingleton<IMaintenanceReportRepository>(repository);
                services.RemoveAll<IDateTimeProvider>();
                services.AddSingleton<IDateTimeProvider>(new FixedPlantDateClock(MaintenanceReportScenario.Today));
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

    private static List<string> PmNos(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("pmNo").GetString()!).ToList();

    private static JsonElement Row(JsonElement page, string pmNo) =>
        page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("pmNo").GetString() == pmNo);

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

    // ================================================================ PM Schedule

    [Fact]
    public async Task Schedule_OpenPmsOfBothWorkflows_EarliestDueFirst_WithEachWorkflowsOwnStatus()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/schedule"));

        Assert.Equal(new[] { "MPM-0002", "MPMD-0001", "MPMD-0004", "MPM-0004", "MPMD-0002", "MPM-0003" }, PmNos(data));
        Assert.Equal("Overdue", Row(data, "MPM-0002").GetProperty("status").GetString());
        Assert.Equal(3, Row(data, "MPM-0002").GetProperty("daysOverdue").GetInt32());
        Assert.Equal("CHK-0001", Row(data, "MPM-0002").GetProperty("planCode").GetString());
        Assert.Equal("Scheduled", Row(data, "MPM-0003").GetProperty("status").GetString());
        Assert.Equal("Preventive", Row(data, "MPM-0003").GetProperty("maintenanceTypeName").GetString());
        Assert.Equal("In Progress", Row(data, "MPM-0004").GetProperty("status").GetString());
        var d1 = Row(data, "MPMD-0001");
        Assert.Equal("Overdue", d1.GetProperty("status").GetString());
        Assert.Equal("Mold", d1.GetProperty("assetType").GetString());
        Assert.Equal("Mold PM", d1.GetProperty("source").GetString());
        Assert.Equal("Shot-based", d1.GetProperty("category").GetString());
        Assert.Equal(460_000, d1.GetProperty("currentUsageShots").GetInt32());
        Assert.Equal(-10_000, d1.GetProperty("remainingShots").GetInt32());
        Assert.Equal("Due", Row(data, "MPMD-0002").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, Row(data, "MPMD-0002").GetProperty("daysOverdue").ValueKind);
        Assert.Equal("In Progress", Row(data, "MPMD-0004").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, Row(data, "MPM-0002").GetProperty("category").ValueKind);
    }

    [Theory]
    [InlineData("?status=Due", new[] { "MPMD-0002" })]
    [InlineData("?status=overdue", new[] { "MPM-0002", "MPMD-0001" })]
    [InlineData("?status=Scheduled", new[] { "MPM-0003" })]
    [InlineData("?status=In%20Progress", new[] { "MPMD-0004", "MPM-0004" })]
    [InlineData("?assetType=machine", new[] { "MPM-0002", "MPM-0004", "MPM-0003" })]
    [InlineData("?assetType=Mold", new[] { "MPMD-0001", "MPMD-0004", "MPMD-0002" })]
    [InlineData("?machineId=1", new[] { "MPM-0002" })]
    [InlineData("?moldId=2", new[] { "MPMD-0002" })]
    [InlineData("?category=Inspection", new[] { "MPMD-0004" })]
    [InlineData("?checklistId=11", new[] { "MPM-0002" })]
    [InlineData("?maintenanceTypeId=21", new[] { "MPM-0003" })]
    [InlineData("?fromDate=2026-09-27", new[] { "MPMD-0004", "MPM-0004", "MPMD-0002", "MPM-0003" })]
    [InlineData("?search=cap%20mold", new[] { "MPMD-0001", "MPMD-0004" })]
    [InlineData("?sortDirection=desc", new[] { "MPM-0003", "MPM-0004", "MPMD-0002", "MPMD-0004", "MPMD-0001", "MPM-0002" })]
    [InlineData("?machineId=1&moldId=1", new string[0])]
    public async Task Schedule_FiltersAndSorting_AreAppliedByTheServer(string queryString, string[] expected)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/schedule" + queryString));

        Assert.Equal(expected, PmNos(data));
        Assert.Equal(expected.Length, data.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Schedule_IsPagedByTheServer_AcrossBothWorkflows()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/schedule?pageNumber=2&pageSize=4"));

        Assert.Equal(new[] { "MPMD-0002", "MPM-0003" }, PmNos(data));
        Assert.Equal(6, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, data.GetProperty("totalPages").GetInt32());
        Assert.Equal(new ReportPage(2, 4), Assert.Single(h.Repository.Calls).Page);
    }

    [Theory]
    [InlineData("?status=Completed", "Status must be one of: Scheduled, Due, Overdue, In Progress.")]
    [InlineData("?assetType=Tool", "AssetType must be one of: Machine, Mold.")]
    [InlineData("?category=Repair", "Category must be one of: Scheduled, Shot-based, Damage Repair, Cleaning, Inspection, Preventive, Replacement.")]
    [InlineData("?sortBy=cost", "SortBy must be one of: date, asset, source, status.")]
    [InlineData("?sortDirection=up", "SortDirection must be one of: asc, desc.")]
    [InlineData("?fromDate=2026-10-01&toDate=2026-09-01", "FromDate must be on or before ToDate.")]
    public async Task Schedule_InvalidFilter_Is400(string queryString, string error)
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/schedule" + queryString), error);
        Assert.Empty(h.Repository.Calls);
    }

    // ================================================================ Completed

    [Fact]
    public async Task Completed_BothWorkflows_NewestCompletionFirst_WithCompletionData()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/completed"));

        Assert.Equal(new[] { "MPM-0001", "MPMD-0003" }, PmNos(data));
        var m1 = Row(data, "MPM-0001");
        Assert.Equal("Completed", m1.GetProperty("status").GetString());
        Assert.Equal("Ravi", m1.GetProperty("performedBy").GetString());
        Assert.Equal("2026-09-20", m1.GetProperty("completedDate").GetString());
        Assert.Equal(1, m1.GetProperty("checklistItemsDone").GetInt32());
        Assert.Equal(2, m1.GetProperty("checklistItemsTotal").GetInt32());
        var d3 = Row(data, "MPMD-0003");
        Assert.Equal(85_000, d3.GetProperty("usageAtCompletion").GetInt32());
        Assert.Equal("Cleaning", d3.GetProperty("category").GetString());
        Assert.Equal(JsonValueKind.Null, d3.GetProperty("remainingShots").ValueKind);
    }

    [Theory]
    [InlineData("?performedBy=KUMAR", new[] { "MPMD-0003" })]
    [InlineData("?fromDate=2026-09-15", new[] { "MPM-0001" })]
    [InlineData("?toDate=2026-09-15", new[] { "MPMD-0003" })]
    [InlineData("?assetType=Mold", new[] { "MPMD-0003" })]
    [InlineData("?checklistId=11", new[] { "MPM-0001" })]
    [InlineData("?sortBy=date&sortDirection=asc", new[] { "MPMD-0003", "MPM-0001" })]
    [InlineData("?search=nothing", new string[0])]
    public async Task Completed_Filters(string queryString, string[] expected)
    {
        var h = CreateHarness();

        Assert.Equal(expected, PmNos(await Data(await h.Client.GetAsync(Url + "/completed" + queryString))));
    }

    [Fact]
    public async Task Completed_RejectsAStatusFilter()
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/completed?status=Completed"), "Status does not apply to the completed report.");
    }

    // ================================================================ Overdue

    [Fact]
    public async Task Overdue_UsesEachWorkflowsRule_WithSummaryOverTheWholeFilteredSet()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/overdue?pageSize=1"));

        var s = data.GetProperty("summary");
        Assert.Equal(2, s.GetProperty("totalOverdue").GetInt32());
        Assert.Equal(1, s.GetProperty("machinePmOverdue").GetInt32());
        Assert.Equal(1, s.GetProperty("moldPmOverdue").GetInt32());
        var page = data.GetProperty("page");
        Assert.Equal(new[] { "MPM-0002" }, PmNos(page)); // most overdue first
        Assert.Equal(2, page.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Overdue_NeverIncludes_DueToday_Future_InProgressMold_OrCompleted()
    {
        var h = CreateHarness();

        var page = (await Data(await h.Client.GetAsync(Url + "/overdue"))).GetProperty("page");

        Assert.Equal(new[] { "MPM-0002", "MPMD-0001" }, PmNos(page));
        Assert.Equal(2, Row(page, "MPMD-0001").GetProperty("daysOverdue").GetInt32());
    }

    [Theory]
    [InlineData("?assetType=Mold", new[] { "MPMD-0001" }, 0, 1)]
    [InlineData("?machineId=2", new string[0], 0, 0)]
    [InlineData("?category=Shot-based", new[] { "MPMD-0001" }, 0, 1)]
    [InlineData("?fromDate=2026-09-26", new[] { "MPMD-0001" }, 0, 1)]
    public async Task Overdue_Filters_AlsoNarrowTheSummary(string queryString, string[] expected, int machine, int mold)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/overdue" + queryString));

        Assert.Equal(expected, PmNos(data.GetProperty("page")));
        Assert.Equal(machine, data.GetProperty("summary").GetProperty("machinePmOverdue").GetInt32());
        Assert.Equal(mold, data.GetProperty("summary").GetProperty("moldPmOverdue").GetInt32());
    }

    [Fact]
    public async Task Overdue_RejectsAStatusFilter()
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/overdue?status=Due"), "Status does not apply to the overdue report.");
    }

    // ================================================================ History

    [Fact]
    public async Task History_EveryPmOfBothWorkflows_NewestMaintenanceFirst()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/history"));

        Assert.Equal(new[] { "MPM-0003", "MPM-0004", "MPMD-0002", "MPMD-0004", "MPMD-0001", "MPM-0002", "MPM-0001", "MPMD-0003" }, PmNos(data));
        Assert.Equal(new[] { "Machine PM", "Mold PM" }, data.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("source").GetString()).Distinct().OrderBy(x => x));
    }

    [Theory]
    [InlineData("?status=completed", new[] { "MPM-0001", "MPMD-0003" })]
    [InlineData("?sortBy=asset&sortDirection=asc", new[] { "MPM-0001", "MPM-0002", "MPM-0004", "MPM-0003", "MPMD-0001", "MPMD-0004", "MPMD-0003", "MPMD-0002" })]
    [InlineData("?sortBy=status&sortDirection=asc", new[] { "MPMD-0003", "MPM-0001", "MPMD-0002", "MPMD-0004", "MPM-0004", "MPM-0002", "MPMD-0001", "MPM-0003" })]
    [InlineData("?sortBy=source&sortDirection=desc", new[] { "MPMD-0002", "MPMD-0004", "MPMD-0001", "MPMD-0003", "MPM-0003", "MPM-0004", "MPM-0002", "MPM-0001" })]
    [InlineData("?search=ravi", new[] { "MPM-0001" })]
    [InlineData("?search=daily", new[] { "MPM-0002", "MPM-0001" })]
    [InlineData("?fromDate=2026-09-20&toDate=2026-09-26", new[] { "MPMD-0001", "MPM-0002", "MPM-0001" })]
    [InlineData("?assetType=Machine&status=Overdue", new[] { "MPM-0002" })]
    public async Task History_FiltersAndSorting(string queryString, string[] expected)
    {
        var h = CreateHarness();

        Assert.Equal(expected, PmNos(await Data(await h.Client.GetAsync(Url + "/history" + queryString))));
    }

    [Fact]
    public async Task History_InvalidStatus_Is400()
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/history?status=Cancelled"),
            "Status must be one of: Scheduled, Due, Overdue, In Progress, Completed.");
    }

    // ================================================================ Lookups

    [Fact]
    public async Task Lookups_ReturnFilterOptions()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/lookups"));

        Assert.Equal(3, data.GetProperty("machines").GetArrayLength());
        Assert.Equal(2, data.GetProperty("molds").GetArrayLength());
        Assert.Equal(new[] { "CHK-0001" }, data.GetProperty("maintenancePlans").EnumerateArray().Select(p => p.GetProperty("code").GetString()));
        Assert.Equal(new[] { "Preventive" }, data.GetProperty("maintenanceTypes").EnumerateArray().Select(p => p.GetProperty("name").GetString()));
        Assert.Equal(MaintenanceReportStatus.Open, data.GetProperty("openStatuses").EnumerateArray().Select(s => s.GetString()));
        Assert.Contains(("MAINT_MANAGER", ModuleCodes.RptMaintenance, PermissionAction.View), h.Authorization.Checks);
    }

    // ================================================================ Export

    [Fact]
    public async Task ExportSchedule_WholeFilteredSortedSet_ReadableHeaders()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync(Url + "/schedule/export?assetType=Mold&pageSize=1");
        var lines = await CsvLines(response);

        Assert.Equal("pm-schedule-report-20260928.csv", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("PM No,Source,Asset Type,Asset Code,Asset Name,Category,Maintenance Plan,Frequency,Maintenance Type,Due Date,Status,Days Overdue,Current Usage (shots),Threshold (shots),Interval (shots),Remaining Shots,Remarks", lines[0]);
        Assert.Equal(new[] { "MPMD-0001", "MPMD-0004", "MPMD-0002" }, lines.Skip(1).Select(l => l.Split(',')[0]));
        Assert.Equal("MPMD-0001,Mold PM,Mold,MLD-0001,Cap Mold,Shot-based,,,,2026-09-26,Overdue,2,460000,450000,50000,-10000,", lines[1]);
        Assert.Null(Assert.Single(h.Repository.Calls).Page);
    }

    [Fact]
    public async Task ExportCompleted_And_History_And_Overdue()
    {
        var h = CreateHarness();

        var completed = await CsvLines(await h.Client.GetAsync(Url + "/completed/export?assetType=Machine"));
        var history = await CsvLines(await h.Client.GetAsync(Url + "/history/export"));
        var overdue = await CsvLines(await h.Client.GetAsync(Url + "/overdue/export"));

        Assert.Equal("MPM-0001,Machine PM,Machine,MAC-0001,Injection Moulding M/c 1,,CHK-0001 - Daily oil,Daily,,2026-09-20,2026-09-20,Ravi,Completed,,,,,1,2,", completed[1]);
        Assert.Equal(2, completed.Length);
        Assert.Equal(9, history.Length);
        Assert.Contains("Performed By", history[0]);
        Assert.Equal(3, overdue.Length);
        Assert.Contains("Days Overdue", overdue[0]);
        Assert.StartsWith("MPM-0002,", overdue[1]);
    }

    [Fact]
    public async Task Export_InvalidFilter_Is400()
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/overdue/export?status=Due"), "Status does not apply to the overdue report.");
    }

    // ================================================================ Cancellation

    [Fact]
    public async Task Service_PassesTheCancellationTokenToTheRepository()
    {
        var repository = new InMemoryMaintenanceReportRepository(new MaintenanceReportScenario());
        var service = new MaintenanceReportService(repository, new FixedPlantDateClock(MaintenanceReportScenario.Today));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetHistoryAsync(new MaintenanceReportQuery(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetOverdueAsync(new MaintenanceReportQuery(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportScheduleAsync(new MaintenanceReportQuery(), cts.Token));
    }

    // ================================================================ Authorization

    public static readonly TheoryData<string> ReportPaths = new() { "/schedule", "/completed", "/overdue", "/history", "/lookups" };
    public static readonly TheoryData<string> ExportPaths = new() { "/schedule/export", "/completed/export", "/overdue/export", "/history/export" };

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
    public async Task Reports_RequireRptMaintenanceView(string path)
    {
        var h = CreateHarness(decide: (_, module, action) => !(module == ModuleCodes.RptMaintenance && action == PermissionAction.View));

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync(Url + path)).StatusCode);
        Assert.Equal(("MAINT_MANAGER", ModuleCodes.RptMaintenance, PermissionAction.View), Assert.Single(h.Authorization.Checks));
        Assert.Empty(h.Repository.Calls);
    }

    [Theory]
    [MemberData(nameof(ExportPaths))]
    public async Task Exports_RequireRptMaintenanceExport_ViewAloneIsNotEnough(string path)
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.RptMaintenance && action == PermissionAction.View);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync(Url + path)).StatusCode);
        Assert.Equal(("MAINT_MANAGER", ModuleCodes.RptMaintenance, PermissionAction.Export), Assert.Single(h.Authorization.Checks));
        Assert.Empty(h.Repository.Calls);
    }

    [Theory]
    [MemberData(nameof(ExportPaths))]
    public async Task Exports_WithExportPermission_Succeed(string path)
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.RptMaintenance && action == PermissionAction.Export);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync(Url + path)).StatusCode);
    }

    [Fact]
    public async Task ReportEndpoints_AskOnlyForTheMaintenanceReportModule()
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.RptMaintenance && action == PermissionAction.View);

        foreach (var path in ReportPaths)
        {
            Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync(Url + (string)path[0])).StatusCode);
        }

        Assert.All(h.Authorization.Checks, c => Assert.Equal(ModuleCodes.RptMaintenance, c.ModuleCode));
    }

    [Fact]
    public async Task TheBareRoute_IsNotAnEndpoint()
    {
        var h = CreateHarness();

        Assert.Equal(HttpStatusCode.NotFound, (await h.Client.GetAsync(Url)).StatusCode);
    }

    [Fact]
    public void TheReportRepository_IsReadOnly()
    {
        var methods = typeof(IMaintenanceReportRepository).GetMethods().Select(m => m.Name).ToList();

        Assert.NotEmpty(methods);
        Assert.All(methods, name => Assert.StartsWith("Get", name));
    }
}
