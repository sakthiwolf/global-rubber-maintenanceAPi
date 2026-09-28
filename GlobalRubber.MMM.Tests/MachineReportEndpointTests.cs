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
/// /api/v1/reports/machines through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model
/// binding) with the REAL MachineReportService and the real MachineReportQueryBuilder filters/sorts (run in memory over
/// MachineReportScenario). Plant "today" is 2026-09-26.
/// </summary>
public class MachineReportEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string Url = "/api/v1/reports/machines";
    private readonly ApiWebApplicationFactory _factory;

    public MachineReportEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemoryMachineReportRepository Repository, StubPermissionAuthorization Authorization);

    private H CreateHarness(bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null)
    {
        var repository = new InMemoryMachineReportRepository(new MachineReportScenario());
        var authorization = new StubPermissionAuthorization(decide ?? ((_, _, _) => true));

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IMachineReportRepository>();
                services.AddSingleton<IMachineReportRepository>(repository);
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

        return new H(client, repository, authorization);
    }

    /// <summary>Real UtcNow (so the minted JWT is valid), fixed plant "today" (so Overdue/Scheduled and file names are stable).</summary>
    private sealed class FixedPlantDateClock : IDateTimeProvider
    {
        public FixedPlantDateClock(DateOnly today) => Today = today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateOnly Today { get; }
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

    private static async Task<string[]> CsvLines(HttpResponseMessage r)
    {
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var bytes = await r.Content.ReadAsByteArrayAsync();
        Assert.True(bytes.Take(3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "UTF-8 BOM expected");
        return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
    }

    // ================================================================ Machine List

    [Fact]
    public async Task MachineList_ReturnsEveryMachine_InCodeOrder_WithDepartmentAndMaintenanceDates()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url));

        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(new[] { "MAC-0001", "MAC-0002", "MAC-0003" }, Col(data, "machineCode"));
        var first = data.GetProperty("items")[0];
        Assert.Equal("Injection Moulding", first.GetProperty("departmentName").GetString());
        Assert.Equal("2026-02-01", first.GetProperty("lastMaintenanceDate").GetString());
        Assert.Equal("2026-03-03", first.GetProperty("nextMaintenanceDate").GetString());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("items")[1].GetProperty("lastMaintenanceDate").ValueKind);
        // The responsible engineer stays hidden (temporarily disabled on Machine - user decision).
        Assert.False(first.TryGetProperty("responsibleEngineerName", out _));
    }

    [Theory]
    [InlineData("?departmentId=2", "MAC-0003")]
    [InlineData("?isActive=false", "MAC-0003")]
    [InlineData("?operationalStatus=breakdown", "MAC-0002")]
    [InlineData("?criticality=HIGH", "MAC-0001")]
    [InlineData("?machineType=Mixing%20Mill", "MAC-0002")]
    [InlineData("?machineId=3", "MAC-0003")]
    [InlineData("?search=bay%201", "MAC-0001")]
    [InlineData("?search=old%20dept", "MAC-0002")]
    [InlineData("?nextMaintenanceFrom=2026-03-01&nextMaintenanceTo=2026-03-31", "MAC-0001")]
    public async Task MachineList_Filters_AreAppliedByTheServer(string queryString, string expectedCode)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + queryString));

        Assert.Equal(new[] { expectedCode }, Col(data, "machineCode"));
        Assert.Equal(1, data.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task MachineList_IsPagedByTheServer()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "?pageNumber=2&pageSize=2"));

        Assert.Equal(new[] { "MAC-0003" }, Col(data, "machineCode"));
        Assert.Equal(3, data.GetProperty("totalCount").GetInt32());
        Assert.Equal(2, data.GetProperty("totalPages").GetInt32());
        Assert.True(data.GetProperty("hasPreviousPage").GetBoolean());
        Assert.Equal(new ReportPage(2, 2), Assert.Single(h.Repository.Calls).Page);
    }

    [Fact]
    public async Task MachineList_NoMatch_ReturnsAnEmptyPage()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "?search=nothing-like-this"));

        Assert.Equal(0, data.GetProperty("totalCount").GetInt32());
        Assert.Empty(data.GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("?criticality=Extreme", "Criticality must be one of: Low, Medium, High.")]
    [InlineData("?operationalStatus=Broken", "OperationalStatus must be one of: Running, Idle, Breakdown, Maintenance.")]
    [InlineData("?nextMaintenanceFrom=2026-04-01&nextMaintenanceTo=2026-03-01", "NextMaintenanceFrom must be on or before NextMaintenanceTo.")]
    public async Task MachineList_InvalidFilter_Is400_WithTheReason(string queryString, string error)
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync(Url + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var root = await Root(response);
        Assert.False(root.GetProperty("success").GetBoolean());
        Assert.Contains(error, root.GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
        Assert.Empty(h.Repository.Calls);
    }

    // ================================================================ Maintenance History

    [Fact]
    public async Task History_NoStatus_ReturnsEveryOccurrence_NewestMaintenanceFirst()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/maintenance-history"));

        // Maintenance date = completed date, else due date: 09-30, 09-25, 09-22, 09-20, 09-11.
        Assert.Equal(new[] { "MPM-0005", "MPM-0004", "MPM-0002", "MPM-0001", "MPM-0003" }, Col(data, "pmNo"));
    }

    [Fact]
    public async Task History_Row_CarriesPlan_PerformedBy_ChecklistCounts_AndType()
    {
        var h = CreateHarness();

        var items = (await Data(await h.Client.GetAsync(Url + "/maintenance-history"))).GetProperty("items").EnumerateArray().ToList();

        var pm1 = items.Single(i => i.GetProperty("pmNo").GetString() == "MPM-0001");
        Assert.Equal("CHK-0001", pm1.GetProperty("checklistCode").GetString());
        Assert.Equal("Daily", pm1.GetProperty("frequency").GetString());
        Assert.Equal("Ravi", pm1.GetProperty("maintenanceBy").GetString());
        Assert.Equal("Completed", pm1.GetProperty("status").GetString());
        Assert.Equal(1, pm1.GetProperty("checklistItemsDone").GetInt32());
        Assert.Equal(2, pm1.GetProperty("checklistItemsTotal").GetInt32());
        Assert.Equal("2026-09-20", pm1.GetProperty("completedDate").GetString());
        var pm3 = items.Single(i => i.GetProperty("pmNo").GetString() == "MPM-0003");
        Assert.Equal("Preventive", pm3.GetProperty("maintenanceTypeName").GetString());
        Assert.Equal(JsonValueKind.Null, pm3.GetProperty("checklistCode").ValueKind);
    }

    [Theory]
    [InlineData("?status=Completed", new[] { "MPM-0002", "MPM-0001", "MPM-0003" })]
    [InlineData("?status=overdue", new[] { "MPM-0004" })]
    [InlineData("?status=Scheduled", new[] { "MPM-0005" })]
    [InlineData("?machineId=2", new[] { "MPM-0005", "MPM-0003" })]
    [InlineData("?maintenanceTypeId=21", new[] { "MPM-0003" })]
    [InlineData("?checklistId=11&status=Completed", new[] { "MPM-0002", "MPM-0001" })]
    [InlineData("?search=ravi", new[] { "MPM-0001", "MPM-0003" })]
    [InlineData("?search=weekly", new[] { "MPM-0005" })]
    [InlineData("?fromDate=2026-09-20&toDate=2026-09-22", new[] { "MPM-0002", "MPM-0001" })]
    [InlineData("?fromDate=2030-01-01", new string[0])]
    public async Task History_Filters_AreAppliedByTheServer(string queryString, string[] expected)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/maintenance-history" + queryString));

        Assert.Equal(expected, Col(data, "pmNo"));
        Assert.Equal(expected.Length, data.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task History_OpenOccurrenceDueBeforeToday_IsReportedAsOverdue()
    {
        var h = CreateHarness();

        var item = (await Data(await h.Client.GetAsync(Url + "/maintenance-history?status=Overdue"))).GetProperty("items")[0];

        Assert.Equal("Overdue", item.GetProperty("status").GetString());
        Assert.True(item.GetProperty("isOverdue").GetBoolean());
    }

    [Theory]
    [InlineData("?status=Pending", "Status must be one of: Completed, Scheduled, Overdue.")]
    [InlineData("?fromDate=2026-09-22&toDate=2026-09-20", "FromDate must be on or before ToDate.")]
    public async Task History_InvalidFilter_Is400(string queryString, string error)
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync(Url + "/maintenance-history" + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(error, (await Root(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
    }

    // ================================================================ Breakdown

    [Fact]
    public async Task Breakdowns_NewestFirst_WithStatus_Engineer_AndIstTimes()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/breakdowns"));

        Assert.Equal(new[] { "BRK-0005", "BRK-0004", "BRK-0003", "BRK-0002", "BRK-0001" }, Col(data, "breakdownNo"));
        var b1 = data.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("breakdownNo").GetString() == "BRK-0001");
        Assert.Equal("Resolved", b1.GetProperty("status").GetString());
        Assert.Equal("Closed", b1.GetProperty("stage").GetString());
        Assert.Equal("Suresh", b1.GetProperty("assignedEngineerName").GetString());
        Assert.Equal("Electrical", b1.GetProperty("breakdownTypeName").GetString());
        Assert.Equal("Op A", b1.GetProperty("reportedBy").GetString());
        Assert.Equal("08:00:00", b1.GetProperty("breakdownTime").GetString());
        // Stored 05:30 UTC -> 11:00 IST.
        Assert.Equal("2026-09-01T11:00:00", b1.GetProperty("resolvedAt").GetString());
        Assert.Equal(2.5m, b1.GetProperty("downtimeHours").GetDecimal());
        Assert.False(b1.TryGetProperty("rowVersion", out _));
    }

    [Theory]
    [InlineData("?status=Open", new[] { "BRK-0004", "BRK-0003" })]
    [InlineData("?status=resolved", new[] { "BRK-0005", "BRK-0002", "BRK-0001" })]
    [InlineData("?stage=Maintenance%20Started", new[] { "BRK-0003" })]
    [InlineData("?priority=critical", new[] { "BRK-0005", "BRK-0002" })]
    [InlineData("?breakdownTypeId=31", new[] { "BRK-0003", "BRK-0001" })]
    [InlineData("?machineId=2", new[] { "BRK-0004", "BRK-0002" })]
    [InlineData("?fromDate=2026-09-05&toDate=2026-09-12", new[] { "BRK-0004", "BRK-0003", "BRK-0002" })]
    [InlineData("?search=suresh", new[] { "BRK-0003", "BRK-0001" })]
    [InlineData("?search=MECHANICAL", new[] { "BRK-0002" })]
    [InlineData("?search=op%20a", new[] { "BRK-0001" })]
    [InlineData("?machineId=3&status=Open", new string[0])]
    public async Task Breakdowns_Filters_AreAppliedByTheServer(string queryString, string[] expected)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/breakdowns" + queryString));

        Assert.Equal(expected, Col(data, "breakdownNo"));
    }

    [Fact]
    public async Task Breakdowns_OpenBreakdown_HasNoDowntime()
    {
        var h = CreateHarness();

        var item = (await Data(await h.Client.GetAsync(Url + "/breakdowns?stage=Maintenance Started"))).GetProperty("items")[0];

        Assert.Equal("Open", item.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("downtimeHours").ValueKind);
        Assert.Equal(JsonValueKind.Null, item.GetProperty("resolvedAt").ValueKind);
    }

    [Theory]
    [InlineData("?priority=Urgent", "Priority must be one of: Low, Medium, High, Critical.")]
    [InlineData("?stage=Done", "Stage must be one of: Reported, Assigned, Maintenance Started, Resolved, Closed.")]
    [InlineData("?status=Closed", "Status must be one of: Open, Resolved.")]
    [InlineData("?sortBy=cost", "SortBy must be one of: date, machine, duration, priority, status.")]
    [InlineData("?sortDirection=up", "SortDirection must be one of: asc, desc.")]
    public async Task Breakdowns_InvalidFilter_Is400(string queryString, string error)
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync(Url + "/breakdowns" + queryString);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(error, (await Root(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
    }

    // ================================================================ Downtime

    [Fact]
    public async Task Downtime_Summary_IsCalculatedOverTheWholeFilteredSet_NotThePage()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/downtime?pageSize=1"));

        var s = data.GetProperty("summary");
        Assert.Equal(5, s.GetProperty("breakdownCount").GetInt32());
        Assert.Equal(2, s.GetProperty("openCount").GetInt32());
        Assert.Equal(3, s.GetProperty("resolvedCount").GetInt32());
        Assert.Equal(3, s.GetProperty("downtimeRecordCount").GetInt32());
        Assert.Equal(9.5m, s.GetProperty("totalDowntimeHours").GetDecimal());
        Assert.Equal(3.17m, s.GetProperty("averageDowntimeHours").GetDecimal());
        Assert.Equal(6.0m, s.GetProperty("longestDowntimeHours").GetDecimal());
        Assert.Equal("BRK-0002", s.GetProperty("longestDowntimeBreakdownNo").GetString());
        Assert.Single(data.GetProperty("page").GetProperty("items").EnumerateArray());
        Assert.Equal(5, data.GetProperty("page").GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Downtime_Summary_FollowsTheFilters()
    {
        var h = CreateHarness();

        var s = (await Data(await h.Client.GetAsync(Url + "/downtime?machineId=1"))).GetProperty("summary");

        Assert.Equal(2, s.GetProperty("breakdownCount").GetInt32());
        Assert.Equal(1, s.GetProperty("openCount").GetInt32());
        Assert.Equal(2.5m, s.GetProperty("totalDowntimeHours").GetDecimal());
        Assert.Equal(2.5m, s.GetProperty("averageDowntimeHours").GetDecimal());
        Assert.Equal("BRK-0001", s.GetProperty("longestDowntimeBreakdownNo").GetString());
    }

    [Fact]
    public async Task Downtime_OnlyOpenBreakdowns_HasNoAverageOrLongest()
    {
        var h = CreateHarness();

        var s = (await Data(await h.Client.GetAsync(Url + "/downtime?status=Open"))).GetProperty("summary");

        Assert.Equal(2, s.GetProperty("openCount").GetInt32());
        Assert.Equal(0m, s.GetProperty("totalDowntimeHours").GetDecimal());
        Assert.Equal(JsonValueKind.Null, s.GetProperty("averageDowntimeHours").ValueKind);
        Assert.Equal(JsonValueKind.Null, s.GetProperty("longestDowntimeHours").ValueKind);
        Assert.Equal(JsonValueKind.Null, s.GetProperty("longestDowntimeBreakdownNo").ValueKind);
    }

    [Fact]
    public async Task Downtime_EmptyRange_ReturnsZeroSummary_AndNoRows()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/downtime?fromDate=2025-01-01&toDate=2025-01-31"));

        Assert.Equal(0, data.GetProperty("summary").GetProperty("breakdownCount").GetInt32());
        Assert.Equal(0, data.GetProperty("page").GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("duration", "desc", new[] { "BRK-0002", "BRK-0001", "BRK-0005", "BRK-0004", "BRK-0003" })]
    [InlineData("duration", "asc", new[] { "BRK-0003", "BRK-0004", "BRK-0005", "BRK-0001", "BRK-0002" })]
    [InlineData("priority", "desc", new[] { "BRK-0005", "BRK-0002", "BRK-0001", "BRK-0004", "BRK-0003" })]
    [InlineData("machine", "asc", new[] { "BRK-0001", "BRK-0003", "BRK-0002", "BRK-0004", "BRK-0005" })]
    [InlineData("date", "asc", new[] { "BRK-0001", "BRK-0002", "BRK-0003", "BRK-0004", "BRK-0005" })]
    public async Task Downtime_Sorting(string sortBy, string direction, string[] expected)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync($"{Url}/downtime?sortBy={sortBy}&sortDirection={direction}"));

        Assert.Equal(expected, Col(data.GetProperty("page"), "breakdownNo"));
    }

    // ================================================================ Lookups

    [Fact]
    public async Task Lookups_ReturnFilterOptions_UnderTheReportPermission()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/lookups"));

        Assert.Equal(3, data.GetProperty("machines").GetArrayLength());
        Assert.Equal(3, data.GetProperty("departments").GetArrayLength());
        Assert.Equal(new[] { "Cutting", "Injection Moulding", "Mixing Mill" },
            data.GetProperty("machineTypes").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(new[] { "Preventive", "Calibration" },
            data.GetProperty("maintenanceTypes").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        Assert.Equal(new[] { "CHK-0001", "CHK-0002" },
            data.GetProperty("maintenancePlans").EnumerateArray().Select(t => t.GetProperty("code").GetString()));
        Assert.Equal(BreakdownStage.All, data.GetProperty("stages").EnumerateArray().Select(t => t.GetString()));
        Assert.Equal(new[] { "Open", "Resolved" }, data.GetProperty("breakdownStatuses").EnumerateArray().Select(t => t.GetString()));
        Assert.Contains((("MAINT_MANAGER", ModuleCodes.RptMachine, PermissionAction.View)), h.Authorization.Checks);
    }

    // ================================================================ Export

    [Fact]
    public async Task ExportMachineList_IsACsv_OfTheWholeFilteredSet_WithReadableHeaders()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync(Url + "/export?isActive=true&pageSize=1&pageNumber=1");
        var lines = await CsvLines(response);

        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("machine-list-report-20260926.csv", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("Machine Code,Machine Name,Machine Type,Department,Location,Criticality,Operational Status,Active Status,Last Maintenance,Next Maintenance", lines[0]);
        Assert.Equal(3, lines.Length); // header + the 2 active machines, paging ignored
        Assert.Equal("MAC-0001,Injection Moulding M/c 1,Injection Moulding,Injection Moulding,Shop Floor 1 - Bay 1,High,Running,Active,2026-02-01,2026-03-03", lines[1]);
        Assert.Null(Assert.Single(h.Repository.Calls).Page);
    }

    [Fact]
    public async Task ExportHistory_RespectsStatusAndSearch()
    {
        var h = CreateHarness();

        var lines = await CsvLines(await h.Client.GetAsync(Url + "/maintenance-history/export?status=Completed&search=ravi"));

        Assert.StartsWith("PM No,Machine Code,Machine Name,Maintenance Plan,Frequency,Maintenance Type,Scheduled Date,Completed Date,Status,Performed By", lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.StartsWith("MPM-0001,MAC-0001,Injection Moulding M/c 1,CHK-0001 - Daily oil,Daily,,2026-09-20,2026-09-20,Completed,Ravi,1,2,", lines[1]);
    }

    [Fact]
    public async Task ExportBreakdowns_RespectsFilters_QuotesCommas_AndUsesIstTimes()
    {
        var h = CreateHarness();

        var lines = await CsvLines(await h.Client.GetAsync(Url + "/breakdowns/export?priority=Critical"));

        Assert.Equal(3, lines.Length);
        Assert.StartsWith("BRK-0005,MAC-0003,Old Machine,2026-09-15,11:00,\"Hydraulic, leak\",,Critical,Closed,Resolved,", lines[1]);
        Assert.Contains("2026-09-05 16:30", lines[2]); // resolved 11:00 UTC -> 16:30 IST
        Assert.DoesNotContain("MachineBreakdownId", lines[0]);
    }

    [Fact]
    public async Task ExportDowntime_MarksOpenBreakdownsAsOngoing_AndFollowsTheSort()
    {
        var h = CreateHarness();

        var lines = await CsvLines(await h.Client.GetAsync(Url + "/downtime/export?sortBy=duration&sortDirection=desc"));

        Assert.Equal("Breakdown No,Machine Code,Machine Name,Breakdown Type,Problem,Priority,Breakdown Start,Maintenance Started,Resolved At,Downtime (hrs),Status", lines[0]);
        Assert.Equal(new[] { "BRK-0002", "BRK-0001", "BRK-0005", "BRK-0004", "BRK-0003" }, lines.Skip(1).Select(l => l.Split(',')[0]));
        Assert.EndsWith(",6.0,Resolved", lines[1]);
        Assert.EndsWith(",Ongoing,Ongoing", lines[5]);
    }

    [Fact]
    public async Task Export_InvalidFilter_Is400_InTheApiResponseShape()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync(Url + "/breakdowns/export?fromDate=2026-09-10&toDate=2026-09-01");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("FromDate must be on or before ToDate.", (await Root(response)).GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
    }

    // ================================================================ Authorization

    [Theory]
    [InlineData("")]
    [InlineData("/maintenance-history")]
    [InlineData("/breakdowns")]
    [InlineData("/downtime")]
    [InlineData("/lookups")]
    [InlineData("/export")]
    [InlineData("/downtime/export")]
    public async Task EveryEndpoint_Without_A_Token_Is401(string path)
    {
        var h = CreateHarness(authenticate: false);

        var response = await h.Client.GetAsync(Url + path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(h.Repository.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/maintenance-history")]
    [InlineData("/breakdowns")]
    [InlineData("/downtime")]
    [InlineData("/lookups")]
    public async Task Reports_RequireRptMachineView(string path)
    {
        var h = CreateHarness(decide: (_, module, action) => !(module == ModuleCodes.RptMachine && action == PermissionAction.View));

        var response = await h.Client.GetAsync(Url + path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(("MAINT_MANAGER", ModuleCodes.RptMachine, PermissionAction.View), Assert.Single(h.Authorization.Checks));
        Assert.Empty(h.Repository.Calls);
    }

    [Theory]
    [InlineData("/export")]
    [InlineData("/maintenance-history/export")]
    [InlineData("/breakdowns/export")]
    [InlineData("/downtime/export")]
    public async Task Exports_RequireRptMachineExport_ViewAloneIsNotEnough(string path)
    {
        // e.g. the seeded MAINT_ENGINEER / PRODUCTION_USER: View but no Export on RPT_MACHINE.
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.RptMachine && action == PermissionAction.View);

        var response = await h.Client.GetAsync(Url + path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(("MAINT_MANAGER", ModuleCodes.RptMachine, PermissionAction.Export), Assert.Single(h.Authorization.Checks));
        Assert.Empty(h.Repository.Calls);
    }

    [Fact]
    public async Task ReportEndpoints_AskForTheReportModule_NotTheTransactionModules()
    {
        // A user with ONLY RPT_MACHINE View can read every tab (no MASTER_MACHINE / TRN_* permission needed).
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.RptMachine && action == PermissionAction.View);

        foreach (var path in new[] { "", "/maintenance-history", "/breakdowns", "/downtime", "/lookups" })
        {
            Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync(Url + path)).StatusCode);
        }

        Assert.All(h.Authorization.Checks, c => Assert.Equal(ModuleCodes.RptMachine, c.ModuleCode));
    }

    [Fact]
    public void TheReportRepository_IsReadOnly()
    {
        var methods = typeof(IMachineReportRepository).GetMethods().Select(m => m.Name).ToList();

        Assert.NotEmpty(methods);
        Assert.All(methods, name => Assert.StartsWith("Get", name));
    }
}
