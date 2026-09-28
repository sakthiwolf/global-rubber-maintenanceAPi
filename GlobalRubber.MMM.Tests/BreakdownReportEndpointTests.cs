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
/// /api/v1/reports/breakdowns through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model
/// binding) with the REAL BreakdownReportService, MachineReportQueryBuilder and BreakdownReportQueryBuilder (in memory over
/// MachineReportScenario - 5 breakdowns, see InMemoryBreakdownReportRepository).
/// </summary>
public class BreakdownReportEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string Url = "/api/v1/reports/breakdowns";
    private readonly ApiWebApplicationFactory _factory;

    public BreakdownReportEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemoryMachineReportRepository Rows, InMemoryBreakdownReportRepository Groups, StubPermissionAuthorization Authorization);

    /// <summary>Real UtcNow (so the minted JWT is valid), fixed plant "today".</summary>
    private sealed class FixedPlantDateClock : IDateTimeProvider
    {
        public FixedPlantDateClock(DateOnly today) => Today = today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateOnly Today { get; }
    }

    private H CreateHarness(bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null)
    {
        var scenario = new MachineReportScenario();
        var rows = new InMemoryMachineReportRepository(scenario);
        var groups = new InMemoryBreakdownReportRepository(scenario);
        var authorization = new StubPermissionAuthorization(decide ?? ((_, _, _) => true));

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMachineReportRepository>();
                services.AddSingleton<IMachineReportRepository>(rows);
                services.RemoveAll<IBreakdownReportRepository>();
                services.AddSingleton<IBreakdownReportRepository>(groups);
                services.RemoveAll<IDateTimeProvider>();
                services.AddSingleton<IDateTimeProvider>(new FixedPlantDateClock(MachineReportScenario.Today));
                services.RemoveAll<IPermissionAuthorizationService>();
                services.AddSingleton<IPermissionAuthorizationService>(authorization);
            });
        });

        var client = factory.CreateClient();
        if (authenticate)
        {
            TestAuth.Authenticate(client, factory, "MAINT_MANAGER", 1);
        }

        return new H(client, rows, groups, authorization);
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

    private static List<string> Nos(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("breakdownNo").GetString()!).ToList();

    private static List<string> Names(JsonElement groups) =>
        groups.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("name").GetString()!).ToList();

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

    // ================================================================ Breakdown List

    [Fact]
    public async Task List_EveryBreakdown_NewestFirst_WithDepartmentStatusAndStoredDowntime()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url));

        Assert.Equal(new[] { "BRK-0005", "BRK-0004", "BRK-0003", "BRK-0002", "BRK-0001" }, Nos(data));
        var b1 = data.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("breakdownNo").GetString() == "BRK-0001");
        Assert.Equal("Injection Moulding", b1.GetProperty("departmentName").GetString());
        Assert.Equal("Resolved", b1.GetProperty("status").GetString());
        Assert.Equal("Closed", b1.GetProperty("stage").GetString());
        Assert.Equal(2.5m, b1.GetProperty("downtimeHours").GetDecimal());
        Assert.Equal("2026-09-01T11:00:00", b1.GetProperty("resolvedAt").GetString()); // 05:30 UTC -> IST
        var b3 = data.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("breakdownNo").GetString() == "BRK-0003");
        Assert.Equal("Open", b3.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, b3.GetProperty("downtimeHours").ValueKind);
    }

    [Theory]
    [InlineData("?departmentId=3", new[] { "BRK-0004", "BRK-0002" })]
    [InlineData("?reportedBy=OP%20A", new[] { "BRK-0001" })]
    [InlineData("?machineId=1", new[] { "BRK-0003", "BRK-0001" })]
    [InlineData("?breakdownTypeId=32", new[] { "BRK-0002" })]
    [InlineData("?priority=critical", new[] { "BRK-0005", "BRK-0002" })]
    [InlineData("?stage=Reported", new[] { "BRK-0004" })]
    [InlineData("?status=Open", new[] { "BRK-0004", "BRK-0003" })]
    [InlineData("?fromDate=2026-09-05&toDate=2026-09-10", new[] { "BRK-0003", "BRK-0002" })]
    [InlineData("?search=hydraulic", new[] { "BRK-0005" })]
    [InlineData("?sortBy=status&sortDirection=asc", new[] { "BRK-0004", "BRK-0003", "BRK-0002", "BRK-0001", "BRK-0005" })]
    [InlineData("?sortBy=priority", new[] { "BRK-0005", "BRK-0002", "BRK-0001", "BRK-0004", "BRK-0003" })]
    [InlineData("?departmentId=99", new string[0])]
    public async Task List_FiltersAndSorting_AreAppliedByTheServer(string queryString, string[] expected)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + queryString));

        Assert.Equal(expected, Nos(data));
        Assert.Equal(expected.Length, data.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task List_IsPagedByTheServer()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "?pageNumber=2&pageSize=2"));

        Assert.Equal(new[] { "BRK-0003", "BRK-0002" }, Nos(data));
        Assert.Equal(3, data.GetProperty("totalPages").GetInt32());
        Assert.Equal(new ReportPage(2, 2), Assert.Single(h.Rows.Calls).Page);
    }

    [Theory]
    [InlineData("?priority=Urgent", "Priority must be one of: Low, Medium, High, Critical.")]
    [InlineData("?stage=Done", "Stage must be one of: Reported, Assigned, Maintenance Started, Resolved, Closed.")]
    [InlineData("?status=Closed", "Status must be one of: Open, Resolved.")]
    [InlineData("?sortBy=count", "SortBy must be one of: date, machine, duration, priority, status.")]
    [InlineData("?sortDirection=up", "SortDirection must be one of: asc, desc.")]
    [InlineData("?fromDate=2026-09-10&toDate=2026-09-01", "FromDate must be on or before ToDate.")]
    public async Task List_InvalidFilter_Is400(string queryString, string error)
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + queryString), error);
        Assert.Empty(h.Rows.Calls);
    }

    // ================================================================ Breakdown Analysis

    [Fact]
    public async Task Analysis_DefaultsToMachine_WithTotalsOverTheWholeFilteredSet()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/analysis?pageSize=2"));

        Assert.Equal("machine", data.GetProperty("groupBy").GetString());
        var s = data.GetProperty("summary");
        Assert.Equal(5, s.GetProperty("breakdownCount").GetInt32());
        Assert.Equal(2, s.GetProperty("openCount").GetInt32());
        Assert.Equal(3, s.GetProperty("resolvedCount").GetInt32());
        Assert.Equal(9.5m, s.GetProperty("totalDowntimeHours").GetDecimal());
        var groups = data.GetProperty("groups");
        Assert.Equal(3, groups.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { "Injection Moulding M/c 1", "Mixing Mill" }, Names(groups));
        var mac1 = groups.GetProperty("items")[0];
        Assert.Equal("MAC-0001", mac1.GetProperty("code").GetString());
        Assert.Equal(2, mac1.GetProperty("breakdownCount").GetInt32());
        Assert.Equal(1, mac1.GetProperty("openCount").GetInt32());
        Assert.Equal(2.5m, mac1.GetProperty("totalDowntimeHours").GetDecimal());
        Assert.Equal(2.5m, mac1.GetProperty("averageDowntimeHours").GetDecimal());
    }

    [Theory]
    [InlineData("?groupBy=type", new[] { "Unspecified", "Electrical", "Mechanical" })] // 2-2-1; on a tie the null (no type) name sorts first, as in SQL
    [InlineData("?groupBy=department", new[] { "Injection Moulding", "Old Dept", "Mixing" })]
    [InlineData("?groupBy=priority", new[] { "Critical", "High", "Low", "Medium" })]
    [InlineData("?groupBy=status", new[] { "Resolved", "Open" })]
    [InlineData("?groupBy=stage", new[] { "Closed", "Maintenance Started", "Reported", "Resolved" })]
    [InlineData("?groupBy=machine&sortBy=downtime", new[] { "Mixing Mill", "Injection Moulding M/c 1", "Old Machine" })]
    [InlineData("?groupBy=machine&sortBy=name", new[] { "Injection Moulding M/c 1", "Mixing Mill", "Old Machine" })]
    [InlineData("?groupBy=machine&status=Resolved&sortBy=name", new[] { "Injection Moulding M/c 1", "Mixing Mill", "Old Machine" })]
    [InlineData("?groupBy=type&departmentId=1", new[] { "Electrical" })]
    [InlineData("?groupBy=type&fromDate=2030-01-01", new string[0])]
    public async Task Analysis_GroupsAndSorts(string queryString, string[] expected)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/analysis" + queryString));

        Assert.Equal(expected, Names(data.GetProperty("groups")));
    }

    [Fact]
    public async Task Analysis_ByType_BreakdownsWithoutATypeAreUnspecified_AndOpenOnesHaveNoDowntime()
    {
        var h = CreateHarness();

        var items = (await Data(await h.Client.GetAsync(Url + "/analysis?groupBy=type"))).GetProperty("groups").GetProperty("items").EnumerateArray().ToList();

        var electrical = items.Single(i => i.GetProperty("name").GetString() == "Electrical");
        Assert.Equal(2, electrical.GetProperty("breakdownCount").GetInt32());
        Assert.Equal(1, electrical.GetProperty("openCount").GetInt32());
        Assert.Equal(1, electrical.GetProperty("downtimeRecordCount").GetInt32()); // the open BRK-0003 has no downtime
        var unspecified = items.Single(i => i.GetProperty("name").GetString() == "Unspecified");
        Assert.Equal(JsonValueKind.Null, unspecified.GetProperty("code").ValueKind);
        Assert.Equal(1.0m, unspecified.GetProperty("totalDowntimeHours").GetDecimal());
    }

    [Theory]
    [InlineData("?groupBy=shift", "GroupBy must be one of: machine, type, department, priority, stage, status.")]
    [InlineData("?sortBy=date", "SortBy must be one of: count, downtime, name.")]
    [InlineData("?status=Pending", "Status must be one of: Open, Resolved.")]
    public async Task Analysis_InvalidFilter_Is400(string queryString, string error)
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/analysis" + queryString), error);
        Assert.Empty(h.Groups.Calls);
    }

    // ================================================================ Downtime Analysis

    [Fact]
    public async Task Downtime_SummaryFromStoredDowntime_OpenBreakdownsExcluded()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/downtime?pageSize=1"));

        var s = data.GetProperty("summary");
        Assert.Equal(5, s.GetProperty("breakdownCount").GetInt32());
        Assert.Equal(2, s.GetProperty("openCount").GetInt32());
        Assert.Equal(9.5m, s.GetProperty("totalDowntimeHours").GetDecimal());
        Assert.Equal(3.17m, s.GetProperty("averageDowntimeHours").GetDecimal());
        Assert.Equal(6.0m, s.GetProperty("longestDowntimeHours").GetDecimal());
        Assert.Equal("BRK-0002", s.GetProperty("longestDowntimeBreakdownNo").GetString());
        Assert.Equal(5, data.GetProperty("page").GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("?departmentId=1", new[] { "BRK-0003", "BRK-0001" }, 2.5)]
    [InlineData("?status=Open", new[] { "BRK-0004", "BRK-0003" }, 0)]
    [InlineData("?sortBy=duration&sortDirection=desc", new[] { "BRK-0002", "BRK-0001", "BRK-0005", "BRK-0004", "BRK-0003" }, 9.5)]
    [InlineData("?sortBy=machine&sortDirection=asc", new[] { "BRK-0001", "BRK-0003", "BRK-0002", "BRK-0004", "BRK-0005" }, 9.5)]
    public async Task Downtime_FiltersAndSorting(string queryString, string[] expected, double total)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/downtime" + queryString));

        Assert.Equal(expected, Nos(data.GetProperty("page")));
        Assert.Equal((decimal)total, data.GetProperty("summary").GetProperty("totalDowntimeHours").GetDecimal());
    }

    // ================================================================ Breakdown History

    [Fact]
    public async Task History_ResolvedBreakdownsOnly_WithTheirStoredStageTimestamps()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/history"));

        Assert.Equal(new[] { "BRK-0005", "BRK-0002", "BRK-0001" }, Nos(data));
        var b1 = data.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("breakdownNo").GetString() == "BRK-0001");
        Assert.Equal("2026-09-01T08:30:00", b1.GetProperty("maintenanceStartedAt").GetString());
        Assert.Equal("2026-09-01T12:00:00", b1.GetProperty("closedAt").GetString());
    }

    [Theory]
    [InlineData("?stage=closed", new[] { "BRK-0005", "BRK-0001" })]
    [InlineData("?status=resolved", new[] { "BRK-0005", "BRK-0002", "BRK-0001" })]
    [InlineData("?machineId=2", new[] { "BRK-0002" })]
    [InlineData("?sortBy=date&sortDirection=asc", new[] { "BRK-0001", "BRK-0002", "BRK-0005" })]
    public async Task History_Filters(string queryString, string[] expected)
    {
        var h = CreateHarness();

        Assert.Equal(expected, Nos(await Data(await h.Client.GetAsync(Url + "/history" + queryString))));
    }

    [Theory]
    [InlineData("?status=Open", "Status does not apply to the history report (it lists resolved breakdowns only).")]
    [InlineData("?stage=Reported", "Stage must be one of: Resolved, Closed.")]
    public async Task History_InvalidFilter_Is400(string queryString, string error)
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/history" + queryString), error);
    }

    // ================================================================ Lookups

    [Fact]
    public async Task Lookups_ReturnFilterOptions()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/lookups"));

        Assert.Equal(3, data.GetProperty("machines").GetArrayLength());
        Assert.Equal(3, data.GetProperty("departments").GetArrayLength());
        Assert.Equal(2, data.GetProperty("breakdownTypes").GetArrayLength());
        Assert.Equal(BreakdownReportGroupBy.All, data.GetProperty("groupBys").EnumerateArray().Select(s => s.GetString()));
        Assert.Equal(new[] { "Resolved", "Closed" }, data.GetProperty("historyStages").EnumerateArray().Select(s => s.GetString()));
        Assert.Contains(("MAINT_MANAGER", ModuleCodes.RptBreakdown, PermissionAction.View), h.Authorization.Checks);
    }

    // ================================================================ Export

    [Fact]
    public async Task ExportList_WholeFilteredSortedSet_WithDepartment_AndOngoingForOpen()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync(Url + "/export?machineId=1&pageSize=1");
        var lines = await CsvLines(response);

        Assert.Equal("breakdown-list-report-20260926.csv", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("Breakdown No,Machine Code,Machine Name,Department,Breakdown Date,Breakdown Time,Problem,Description,Breakdown Type,Priority,Stage,Status,Reported By,Assigned To,Maintenance Started,Resolved At,Downtime (hrs)", lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("BRK-0003,MAC-0001,Injection Moulding M/c 1,Injection Moulding,2026-09-10,09:00,Loose wire,,Electrical,Low,Maintenance Started,Open,", lines[1]);
        Assert.EndsWith(",Ongoing", lines[1]);
        Assert.EndsWith(",2026-09-01 08:30,2026-09-01 11:00,2.5", lines[2]);
        Assert.Null(Assert.Single(h.Rows.Calls).Page);
    }

    [Fact]
    public async Task ExportAnalysis_ByType()
    {
        var h = CreateHarness();

        var lines = await CsvLines(await h.Client.GetAsync(Url + "/analysis/export?groupBy=type"));

        Assert.Equal("Breakdown Type,Code,Breakdowns,Open,Resolved,Resolved with Downtime,Total Downtime (hrs),Average Downtime (hrs),Longest Downtime (hrs)", lines[0]);
        Assert.Equal("Unspecified,,2,1,1,1,1.0,1.0,1.0", lines[1]); // BRK-0004 (open) + BRK-0005 (1.0 h)
        Assert.Equal("Electrical,BT-0001,2,1,1,1,2.5,2.5,2.5", lines[2]);
        Assert.Equal("Mechanical,BT-0002,1,0,1,1,6.0,6.0,6.0", lines[3]);
        Assert.Null(Assert.Single(h.Groups.Calls).Page);
    }

    [Fact]
    public async Task ExportDowntime_And_History()
    {
        var h = CreateHarness();

        var downtime = await CsvLines(await h.Client.GetAsync(Url + "/downtime/export?sortBy=duration&sortDirection=desc"));
        var history = await CsvLines(await h.Client.GetAsync(Url + "/history/export"));

        Assert.Equal(new[] { "BRK-0002", "BRK-0001", "BRK-0005", "BRK-0004", "BRK-0003" }, downtime.Skip(1).Select(l => l.Split(',')[0]));
        Assert.EndsWith(",Ongoing,Ongoing", downtime[5]);
        Assert.Equal(4, history.Length);
        Assert.Contains("Root Cause,Corrective Action", history[0]);
        Assert.Contains("2026-09-01 12:00", history[3]); // BRK-0001 closed, IST
    }

    // ================================================================ Cancellation

    [Fact]
    public async Task Service_PassesTheCancellationTokenToTheRepositories()
    {
        var scenario = new MachineReportScenario();
        var service = new BreakdownReportService(new CancellingRows(), new InMemoryBreakdownReportRepository(scenario), new FixedPlantDateClock(MachineReportScenario.Today));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetListAsync(new MachineBreakdownReportQuery(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetAnalysisAsync(new BreakdownAnalysisReportQuery(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportHistoryAsync(new MachineBreakdownReportQuery(), cts.Token));
    }

    /// <summary>A rows repository that, like EF, throws once the token is cancelled.</summary>
    private sealed class CancellingRows : IMachineReportRepository
    {
        public Task<(IReadOnlyList<MachineListReportItemDto> Items, int TotalCount)> GetMachineListAsync(MachineListReportQuery q, ReportPage? p, CancellationToken ct) => Throw<(IReadOnlyList<MachineListReportItemDto>, int)>(ct);
        public Task<(IReadOnlyList<MachineMaintenanceHistoryReportItemDto> Items, int TotalCount)> GetMaintenanceHistoryAsync(MachineMaintenanceHistoryReportQuery q, DateOnly t, ReportPage? p, CancellationToken ct) => Throw<(IReadOnlyList<MachineMaintenanceHistoryReportItemDto>, int)>(ct);
        public Task<(IReadOnlyList<MachineBreakdownReportItemDto> Items, int TotalCount)> GetBreakdownsAsync(MachineBreakdownReportQuery q, ReportPage? p, CancellationToken ct) => Throw<(IReadOnlyList<MachineBreakdownReportItemDto>, int)>(ct);
        public Task<MachineDowntimeSummaryDto> GetDowntimeSummaryAsync(MachineBreakdownReportQuery q, CancellationToken ct) => Throw<MachineDowntimeSummaryDto>(ct);
        public Task<MachineReportLookupsDto> GetLookupsAsync(CancellationToken ct) => Throw<MachineReportLookupsDto>(ct);

        private static Task<T> Throw<T>(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            throw new InvalidOperationException("The token should have been cancelled.");
        }
    }

    // ================================================================ Authorization

    public static readonly TheoryData<string> ReportPaths = new() { "", "/analysis", "/downtime", "/history", "/lookups" };
    public static readonly TheoryData<string> ExportPaths = new() { "/export", "/analysis/export", "/downtime/export", "/history/export" };

    [Theory]
    [MemberData(nameof(ReportPaths))]
    [MemberData(nameof(ExportPaths))]
    public async Task EveryEndpoint_Without_A_Token_Is401(string path)
    {
        var h = CreateHarness(authenticate: false);

        Assert.Equal(HttpStatusCode.Unauthorized, (await h.Client.GetAsync(Url + path)).StatusCode);
        Assert.Empty(h.Rows.Calls);
        Assert.Empty(h.Groups.Calls);
    }

    [Theory]
    [MemberData(nameof(ReportPaths))]
    public async Task Reports_RequireRptBreakdownView(string path)
    {
        var h = CreateHarness(decide: (_, module, action) => !(module == ModuleCodes.RptBreakdown && action == PermissionAction.View));

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync(Url + path)).StatusCode);
        Assert.Equal(("MAINT_MANAGER", ModuleCodes.RptBreakdown, PermissionAction.View), Assert.Single(h.Authorization.Checks));
    }

    [Theory]
    [MemberData(nameof(ExportPaths))]
    public async Task Exports_RequireRptBreakdownExport_ViewAloneIsNotEnough(string path)
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.RptBreakdown && action == PermissionAction.View);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync(Url + path)).StatusCode);
        Assert.Equal(("MAINT_MANAGER", ModuleCodes.RptBreakdown, PermissionAction.Export), Assert.Single(h.Authorization.Checks));
        Assert.Empty(h.Rows.Calls);
        Assert.Empty(h.Groups.Calls);
    }

    [Theory]
    [MemberData(nameof(ExportPaths))]
    public async Task Exports_WithExportPermission_Succeed(string path)
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.RptBreakdown && action == PermissionAction.Export);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync(Url + path)).StatusCode);
    }

    [Fact]
    public async Task ReportEndpoints_AskOnlyForTheBreakdownReportModule_NotMachineReportsOrTheTransaction()
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.RptBreakdown && action == PermissionAction.View);

        foreach (var path in ReportPaths)
        {
            Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync(Url + (string)path[0])).StatusCode);
        }

        Assert.All(h.Authorization.Checks, c => Assert.Equal(ModuleCodes.RptBreakdown, c.ModuleCode));
    }

    [Fact]
    public void TheReportRepository_IsReadOnly()
    {
        var methods = typeof(IBreakdownReportRepository).GetMethods().Select(m => m.Name).ToList();

        Assert.NotEmpty(methods);
        Assert.All(methods, name => Assert.StartsWith("Get", name));
    }
}
