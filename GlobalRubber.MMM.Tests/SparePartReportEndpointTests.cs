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
/// /api/v1/reports/spare-parts through the real HTTP pipeline (JWT, [RequirePermission], GlobalExceptionHandler, model
/// binding) with the REAL SparePartReportService and SparePartReportQueryBuilder (in memory over SparePartReportScenario).
/// </summary>
public class SparePartReportEndpointTests : IClassFixture<ApiWebApplicationFactory>
{
    private const string Url = "/api/v1/reports/spare-parts";
    private readonly ApiWebApplicationFactory _factory;

    public SparePartReportEndpointTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
    }

    private sealed record H(HttpClient Client, InMemorySparePartReportRepository Repository, StubPermissionAuthorization Authorization);

    /// <summary>Real UtcNow (so the minted JWT is valid), fixed plant "today".</summary>
    private sealed class FixedPlantDateClock : IDateTimeProvider
    {
        public FixedPlantDateClock(DateOnly today) => Today = today;
        public DateTime UtcNow => DateTime.UtcNow;
        public DateOnly Today { get; }
    }

    private H CreateHarness(bool authenticate = true, Func<string, string, PermissionAction, bool>? decide = null)
    {
        var repository = new InMemorySparePartReportRepository(new SparePartReportScenario());
        var authorization = new StubPermissionAuthorization(decide ?? ((_, _, _) => true));

        var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<ISparePartReportRepository>();
                services.AddSingleton<ISparePartReportRepository>(repository);
                services.RemoveAll<IDateTimeProvider>();
                services.AddSingleton<IDateTimeProvider>(new FixedPlantDateClock(SparePartReportScenario.Today));
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
        page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty(property).ToString()).ToList();

    private static JsonElement Row(JsonElement page, string property, string value) =>
        page.GetProperty("items").EnumerateArray().Single(i => i.GetProperty(property).ToString() == value);

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

    // ================================================================ Spare Part Stock

    [Fact]
    public async Task Stock_EveryPart_WithMasterFigures_StoredStatus_AndLedgerCheck()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url));

        Assert.Equal(new[] { "SPR-0001", "SPR-0002", "SPR-0003", "SPR-0004" }, Col(data, "sparePartCode"));
        var p1 = Row(data, "sparePartCode", "SPR-0001");
        Assert.Equal("Out of Stock", p1.GetProperty("stockStatus").GetString());
        Assert.Equal("MAC-0001", p1.GetProperty("machineCode").GetString());
        Assert.Equal("Acme Bearings", p1.GetProperty("vendorName").GetString());
        Assert.Equal(10.00m, p1.GetProperty("unitCost").GetDecimal());
        Assert.Equal("Matches", p1.GetProperty("ledgerCheck").GetString()); // latest ledger row: 0
        Assert.Equal("No Ledger", Row(data, "sparePartCode", "SPR-0003").GetProperty("ledgerCheck").GetString());
        var p4 = Row(data, "sparePartCode", "SPR-0004");
        Assert.Equal("Mismatch", p4.GetProperty("ledgerCheck").GetString());
        Assert.Equal(7, p4.GetProperty("ledgerStock").GetInt32());
        Assert.Equal(8, p4.GetProperty("currentStock").GetInt32()); // reported, never corrected
        Assert.False(p1.TryGetProperty("reorderQuantity", out _));
    }

    [Theory]
    [InlineData("?isActive=false", new[] { "SPR-0003" })]
    [InlineData("?stockStatus=low%20stock", new[] { "SPR-0002" })]
    [InlineData("?vendorId=1", new[] { "SPR-0001" })]
    [InlineData("?machineId=1", new[] { "SPR-0001" })]
    [InlineData("?category=Belts", new[] { "SPR-0003", "SPR-0004" })]
    [InlineData("?search=GREASE", new[] { "SPR-0002" })]
    [InlineData("?sortBy=currentStock", new[] { "SPR-0003", "SPR-0004", "SPR-0002", "SPR-0001" })]
    [InlineData("?sortBy=stockStatus", new[] { "SPR-0001", "SPR-0002", "SPR-0004", "SPR-0003" })]
    [InlineData("?sortBy=unitCost&sortDirection=asc", new[] { "SPR-0002", "SPR-0004", "SPR-0003", "SPR-0001" })]
    [InlineData("?sortBy=name", new[] { "SPR-0001", "SPR-0003", "SPR-0002", "SPR-0004" })]
    [InlineData("?search=nothing", new string[0])]
    public async Task Stock_FiltersAndSorting(string queryString, string[] expected)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + queryString));

        Assert.Equal(expected, Col(data, "sparePartCode"));
        Assert.Equal(expected.Length, data.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task Stock_IsPagedByTheServer()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "?pageNumber=2&pageSize=3"));

        Assert.Equal(new[] { "SPR-0004" }, Col(data, "sparePartCode"));
        Assert.Equal(2, data.GetProperty("totalPages").GetInt32());
        Assert.Equal(new ReportPage(2, 3), Assert.Single(h.Repository.Calls).Page);
    }

    [Theory]
    [InlineData("?stockStatus=Critical", "StockStatus must be one of: Available, Low Stock, Out of Stock.")]
    [InlineData("?sortBy=stockValue", "SortBy must be one of: code, name, currentStock, minimumStock, unitCost, stockStatus.")]
    [InlineData("?sortDirection=up", "SortDirection must be one of: asc, desc.")]
    public async Task Stock_InvalidFilter_Is400(string queryString, string error)
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + queryString), error);
        Assert.Empty(h.Repository.Calls);
    }

    // ================================================================ Low Stock

    [Fact]
    public async Task LowStock_UsesTheStoredStatus_MostUrgentFirst_WithSummaryOverTheFilteredSet()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/low-stock?pageSize=1"));

        var s = data.GetProperty("summary");
        Assert.Equal(2, s.GetProperty("totalAttention").GetInt32());
        Assert.Equal(1, s.GetProperty("lowStockCount").GetInt32());
        Assert.Equal(1, s.GetProperty("outOfStockCount").GetInt32());
        Assert.Equal(new[] { "SPR-0001" }, Col(data.GetProperty("page"), "sparePartCode"));
        Assert.Equal(2, data.GetProperty("page").GetProperty("totalCount").GetInt32());
    }

    [Theory]
    [InlineData("?stockStatus=Out%20of%20Stock", new[] { "SPR-0001" }, 1)]
    [InlineData("?sortBy=currentStock&sortDirection=desc", new[] { "SPR-0002", "SPR-0001" }, 2)]
    [InlineData("?vendorId=1", new[] { "SPR-0001" }, 1)]
    [InlineData("?isActive=false", new string[0], 0)]
    public async Task LowStock_Filters(string queryString, string[] expected, int total)
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/low-stock" + queryString));

        Assert.Equal(expected, Col(data.GetProperty("page"), "sparePartCode"));
        Assert.Equal(total, data.GetProperty("summary").GetProperty("totalAttention").GetInt32());
    }

    [Fact]
    public async Task LowStock_RejectsAvailable()
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/low-stock?stockStatus=Available"), "StockStatus must be one of: Low Stock, Out of Stock.");
    }

    // ================================================================ Stock Valuation

    [Fact]
    public async Task Valuation_CurrentStockTimesCurrentUnitCost_LargestFirst()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/valuation"));

        var s = data.GetProperty("summary");
        Assert.Equal(4, s.GetProperty("totalParts").GetInt32());
        Assert.Equal(1, s.GetProperty("partsWithoutUnitCost").GetInt32());
        Assert.Equal(58.00m, s.GetProperty("totalValue").GetDecimal());
        Assert.Equal(0.00m, s.GetProperty("lowStockValue").GetDecimal());
        var page = data.GetProperty("page");
        Assert.Equal(new[] { "SPR-0003", "SPR-0004", "SPR-0001", "SPR-0002" }, Col(page, "sparePartCode"));
        Assert.Equal(50.00m, Row(page, "sparePartCode", "SPR-0003").GetProperty("stockValue").GetDecimal());
        Assert.Equal(JsonValueKind.Null, Row(page, "sparePartCode", "SPR-0002").GetProperty("stockValue").ValueKind);
    }

    [Fact]
    public async Task Valuation_FiltersNarrowTheSummary()
    {
        var h = CreateHarness();

        var s = (await Data(await h.Client.GetAsync(Url + "/valuation?isActive=true"))).GetProperty("summary");

        Assert.Equal(3, s.GetProperty("totalParts").GetInt32());
        Assert.Equal(8.00m, s.GetProperty("totalValue").GetDecimal());
    }

    // ================================================================ Stock Movement

    [Fact]
    public async Task Movements_TheLedger_NewestFirst_SignedQuantity_IstTime_UserName()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/movements"));

        Assert.Equal(new[] { "4", "2", "3", "1" }, Col(data, "stockTransactionId"));
        var issue = Row(data, "stockTransactionId", "4");
        Assert.Equal("Issue", issue.GetProperty("transactionType").GetString());
        Assert.Equal("Out", issue.GetProperty("direction").GetString());
        Assert.Equal(-10, issue.GetProperty("quantity").GetInt32());
        Assert.Equal(10, issue.GetProperty("previousStock").GetInt32());
        Assert.Equal(0, issue.GetProperty("newStock").GetInt32());
        Assert.Equal("2026-09-05T08:30:00", issue.GetProperty("transactionAt").GetString());
        Assert.Equal("Admin User", issue.GetProperty("createdByName").GetString());
        Assert.Equal("SPU-0001 (MPM-0001)", issue.GetProperty("referenceNo").GetString());
        Assert.Equal(JsonValueKind.Null, Row(data, "stockTransactionId", "3").GetProperty("createdByName").ValueKind);
    }

    [Theory]
    [InlineData("?fromDate=2026-09-02&toDate=2026-09-02", new[] { "1" })] // 20:00 UTC on 09-01 = 01:30 IST on 09-02
    [InlineData("?toDate=2026-09-04", new[] { "3", "1" })] // 18:40 UTC on 09-04 is already 09-05 in IST
    [InlineData("?direction=in", new[] { "2", "3", "1" })]
    [InlineData("?transactionType=Issue", new[] { "4" })]
    [InlineData("?sparePartId=1", new[] { "4", "1" })]
    [InlineData("?search=spu-0001", new[] { "4" })]
    [InlineData("?sortBy=quantity&sortDirection=asc", new[] { "4", "2", "3", "1" })]
    [InlineData("?sortBy=sparePart", new[] { "4", "1", "2", "3" })]
    [InlineData("?sortBy=newStock", new[] { "1", "3", "2", "4" })]
    [InlineData("?fromDate=2030-01-01", new string[0])]
    public async Task Movements_Filters(string queryString, string[] expected)
    {
        var h = CreateHarness();

        Assert.Equal(expected, Col(await Data(await h.Client.GetAsync(Url + "/movements" + queryString)), "stockTransactionId"));
    }

    [Theory]
    [InlineData("?direction=Sideways", "Direction must be one of: In, Out.")]
    [InlineData("?transactionType=Transfer", "TransactionType must be one of: Opening, Issue, Reversal, Adjustment.")]
    [InlineData("?fromDate=2026-09-10&toDate=2026-09-01", "FromDate must be on or before ToDate.")]
    public async Task Movements_InvalidFilter_Is400(string queryString, string error)
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/movements" + queryString), error);
    }

    // ================================================================ Spare Part Usage

    [Fact]
    public async Task Usage_CostUsesTheUnitCostAtIssue_NotTodaysCost()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/usage"));

        Assert.Equal(new[] { "SPU-0002", "SPU-0001" }, Col(data, "usageNo"));
        var u1 = Row(data, "usageNo", "SPU-0001");
        Assert.Equal(12.50m, u1.GetProperty("unitCostAtIssue").GetDecimal());
        Assert.Equal(50.00m, u1.GetProperty("totalCost").GetDecimal()); // master cost is now 10.00
        Assert.Equal("MPM-0001", u1.GetProperty("machinePmNo").GetString());
        Assert.Equal("MAC-0002", u1.GetProperty("machineCode").GetString());
        Assert.Equal("Ravi", u1.GetProperty("usedByName").GetString());
        Assert.Equal("Admin User", u1.GetProperty("createdByName").GetString());
        var u2 = Row(data, "usageNo", "SPU-0002");
        Assert.Equal("Reversed", u2.GetProperty("status").GetString());
        Assert.Equal("2026-09-06T09:30:00", u2.GetProperty("reversedAt").GetString());
        Assert.Equal("Wrong part", u2.GetProperty("reversalReason").GetString());
        Assert.Equal(JsonValueKind.Null, u2.GetProperty("totalCost").ValueKind);
        Assert.Equal("MPMD-0001", u2.GetProperty("moldPmNo").GetString());
    }

    [Theory]
    [InlineData("?status=posted", new[] { "SPU-0001" })]
    [InlineData("?usedFor=mold%20maintenance", new[] { "SPU-0002" })]
    [InlineData("?moldId=1", new[] { "SPU-0002" })]
    [InlineData("?machineId=2", new[] { "SPU-0001" })]
    [InlineData("?sparePartId=1", new[] { "SPU-0001" })]
    [InlineData("?fromDate=2026-09-06", new[] { "SPU-0002" })]
    [InlineData("?search=ravi", new[] { "SPU-0001" })]
    [InlineData("?sortBy=totalCost", new[] { "SPU-0001", "SPU-0002" })]
    [InlineData("?sortBy=quantity&sortDirection=asc", new[] { "SPU-0002", "SPU-0001" })]
    [InlineData("?sortBy=status", new[] { "SPU-0001", "SPU-0002" })]
    [InlineData("?usedFor=Work%20Order", new string[0])]
    public async Task Usage_Filters(string queryString, string[] expected)
    {
        var h = CreateHarness();

        Assert.Equal(expected, Col(await Data(await h.Client.GetAsync(Url + "/usage" + queryString)), "usageNo"));
    }

    [Theory]
    [InlineData("?usedFor=Repair", "UsedFor must be one of: Machine Breakdown, Machine Maintenance, Mold Maintenance, Work Order, Other.")]
    [InlineData("?status=Cancelled", "Status must be one of: Posted, Reversed.")]
    [InlineData("?sortBy=newStock", "SortBy must be one of: date, sparePart, quantity, totalCost, status.")]
    public async Task Usage_InvalidFilter_Is400(string queryString, string error)
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/usage" + queryString), error);
    }

    // ================================================================ Lookups

    [Fact]
    public async Task Lookups_ReturnOnlyWhatTheFiltersUse()
    {
        var h = CreateHarness();

        var data = await Data(await h.Client.GetAsync(Url + "/lookups"));

        Assert.Equal(4, data.GetProperty("spareParts").GetArrayLength());
        Assert.Equal(new[] { "MAC-0001", "MAC-0002" }, data.GetProperty("machines").EnumerateArray().Select(m => m.GetProperty("code").GetString()));
        Assert.Equal(new[] { "Bearings", "Belts", "Lubricants" }, data.GetProperty("categories").EnumerateArray().Select(c => c.GetString()));
        Assert.Equal(new[] { "Opening", "Issue", "Reversal", "Adjustment" }, data.GetProperty("transactionTypes").EnumerateArray().Select(c => c.GetString()));
        Assert.Contains(("MAINT_MANAGER", ModuleCodes.RptSparePart, PermissionAction.View), h.Authorization.Checks);
    }

    // ================================================================ Export

    [Fact]
    public async Task ExportStock_WholeFilteredSortedSet_WithLedgerCheck()
    {
        var h = CreateHarness();

        var response = await h.Client.GetAsync(Url + "/export?category=Belts&pageSize=1");
        var lines = await CsvLines(response);

        Assert.Equal("spare-part-stock-report-20260928.csv", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
        Assert.Equal("Spare Part Code,Spare Part Name,Unit,Current Stock,Minimum Stock,Stock Status,Unit Cost,Category,Part Number,Machine,Vendor,Store Location,Active Status,Ledger Stock,Ledger Check", lines[0]);
        Assert.Equal(3, lines.Length);
        Assert.Equal("SPR-0004,Seal,Nos,8,2,Available,1.00,Belts,,,,,Active,7,Mismatch", lines[2]);
        Assert.Null(Assert.Single(h.Repository.Calls).Page);
    }

    [Fact]
    public async Task ExportUsage_CostAtIssue_AndIstTimes()
    {
        var h = CreateHarness();

        var lines = await CsvLines(await h.Client.GetAsync(Url + "/usage/export?sortBy=date&sortDirection=asc"));

        Assert.StartsWith("Usage No,Usage Date,Spare Part Code,Spare Part Name,Quantity,Unit,Unit Cost at Issue,Total Cost,Used For", lines[0]);
        Assert.StartsWith("SPU-0001,2026-09-05,SPR-0001,Bearing,4,Nos,12.50,50.00,Machine Maintenance,MAC-0002 - Mixing Mill,,MPM-0001,,Ravi,Posted,2026-09-05 08:30,Admin User", lines[1]);
        Assert.Contains("2026-09-06 09:30,Wrong part", lines[2]);
    }

    [Fact]
    public async Task ExportMovements_Valuation_AndLowStock()
    {
        var h = CreateHarness();

        var movements = await CsvLines(await h.Client.GetAsync(Url + "/movements/export"));
        var valuation = await CsvLines(await h.Client.GetAsync(Url + "/valuation/export"));
        var low = await CsvLines(await h.Client.GetAsync(Url + "/low-stock/export"));

        Assert.Equal("Transaction Date,Spare Part Code,Spare Part Name,Transaction Type,Direction,Quantity,Unit,Previous Stock,New Stock,Reference,Created By,Remarks", movements[0]);
        Assert.Equal("2026-09-05 08:30,SPR-0001,Bearing,Issue,Out,-10,Nos,10,0,SPU-0001 (MPM-0001),Admin User,", movements[1]); // -10 stays a number
        Assert.Equal("SPR-0003,Belt,Nos,20,2.50,50.00,Available", valuation[1]);
        Assert.Equal("SPR-0002,Grease,Kg,3,,,Low Stock", valuation[4]); // no unit cost -> no value
        Assert.Equal(3, low.Length);
    }

    [Fact]
    public async Task Export_InvalidFilter_Is400()
    {
        var h = CreateHarness();

        await AssertBadRequest(await h.Client.GetAsync(Url + "/movements/export?direction=Up"), "Direction must be one of: In, Out.");
    }

    // ================================================================ Cancellation

    [Fact]
    public async Task Service_PassesTheCancellationTokenToTheRepository()
    {
        var service = new SparePartReportService(new InMemorySparePartReportRepository(new SparePartReportScenario()), new FixedPlantDateClock(SparePartReportScenario.Today));
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetStockAsync(new SparePartStockReportQuery(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.GetValuationAsync(new SparePartStockReportQuery(), cts.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportUsageAsync(new SparePartUsageReportQuery(), cts.Token));
    }

    // ================================================================ Authorization

    public static readonly TheoryData<string> ReportPaths = new() { "", "/movements", "/usage", "/low-stock", "/valuation", "/lookups" };
    public static readonly TheoryData<string> ExportPaths = new() { "/export", "/movements/export", "/usage/export", "/low-stock/export", "/valuation/export" };

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
    public async Task Reports_RequireRptSparePartView(string path)
    {
        var h = CreateHarness(decide: (_, module, action) => !(module == ModuleCodes.RptSparePart && action == PermissionAction.View));

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync(Url + path)).StatusCode);
        Assert.Equal(("MAINT_MANAGER", ModuleCodes.RptSparePart, PermissionAction.View), Assert.Single(h.Authorization.Checks));
        Assert.Empty(h.Repository.Calls);
    }

    [Theory]
    [MemberData(nameof(ExportPaths))]
    public async Task Exports_RequireRptSparePartExport_ViewAloneIsNotEnough(string path)
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.RptSparePart && action == PermissionAction.View);

        Assert.Equal(HttpStatusCode.Forbidden, (await h.Client.GetAsync(Url + path)).StatusCode);
        Assert.Equal(("MAINT_MANAGER", ModuleCodes.RptSparePart, PermissionAction.Export), Assert.Single(h.Authorization.Checks));
        Assert.Empty(h.Repository.Calls);
    }

    [Theory]
    [MemberData(nameof(ExportPaths))]
    public async Task Exports_WithExportPermission_Succeed(string path)
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.RptSparePart && action == PermissionAction.Export);

        Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync(Url + path)).StatusCode);
    }

    [Fact]
    public async Task ReportEndpoints_AskOnlyForTheSparePartReportModule()
    {
        var h = CreateHarness(decide: (_, module, action) => module == ModuleCodes.RptSparePart && action == PermissionAction.View);

        foreach (var path in ReportPaths)
        {
            Assert.Equal(HttpStatusCode.OK, (await h.Client.GetAsync(Url + (string)path[0])).StatusCode);
        }

        Assert.All(h.Authorization.Checks, c => Assert.Equal(ModuleCodes.RptSparePart, c.ModuleCode));
    }

    [Fact]
    public void TheReportRepository_IsReadOnly()
    {
        var methods = typeof(ISparePartReportRepository).GetMethods().Select(m => m.Name).ToList();

        Assert.NotEmpty(methods);
        Assert.All(methods, name => Assert.StartsWith("Get", name));
    }
}
