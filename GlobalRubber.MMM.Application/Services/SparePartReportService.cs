using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services.Reports;
using GlobalRubber.MMM.Domain.Constants;
using static GlobalRubber.MMM.Application.Common.ReportFilterValidation;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Spare Part Reports - read-only. Existing rules, reused unchanged:
///   stock status  - the persisted computed stock_status (BR-29; SparePartStockRules.StatusOf mirrors it): current &lt;= 0
///                   Out of Stock, current &lt;= minimum Low Stock, else Available. Low Stock = the two non-Available states.
///   stock value   - current stock x CURRENT master unit cost (no costing method exists in the project).
///   usage cost    - quantity x unit_cost_at_issue (the snapshot taken at issue) - never today's cost.
///   ledger        - transactions.spare_part_stock_transaction as written by the Spare Part / Usage workflows.
/// The master stock is compared with the latest ledger row and a difference is REPORTED (LedgerCheck), never corrected.
/// No reorder quantity, lead time or FIFO/LIFO valuation: none exists in the database. The report creates no notifications.
/// </summary>
public sealed class SparePartReportService : ISparePartReportService
{
    private readonly ISparePartReportRepository _repository;
    private readonly IDateTimeProvider _dateTimeProvider;

    public SparePartReportService(ISparePartReportRepository repository, IDateTimeProvider dateTimeProvider)
    {
        _repository = repository;
        _dateTimeProvider = dateTimeProvider;
    }

    // ================================================================ Screen (paged)

    public async Task<PagedResult<SparePartStockItemDto>> GetStockAsync(SparePartStockReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query, SparePartStockScope.Stock);
        var (items, total) = await _repository.GetStockAsync(query, SparePartStockScope.Stock, PageOf(query), cancellationToken);
        return PagedResult<SparePartStockItemDto>.Create(WithLedgerCheck(items), query.PageNumber, query.PageSize, total);
    }

    public async Task<SparePartLowStockReportDto> GetLowStockAsync(SparePartStockReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query, SparePartStockScope.LowStock);
        var summary = await _repository.GetLowStockSummaryAsync(query, cancellationToken);
        var (items, total) = await _repository.GetStockAsync(query, SparePartStockScope.LowStock, PageOf(query), cancellationToken);
        return new SparePartLowStockReportDto
        {
            Summary = summary,
            Page = PagedResult<SparePartStockItemDto>.Create(WithLedgerCheck(items), query.PageNumber, query.PageSize, total),
        };
    }

    public async Task<SparePartValuationReportDto> GetValuationAsync(SparePartStockReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query, SparePartStockScope.Valuation);
        var summary = await _repository.GetValuationSummaryAsync(query, cancellationToken);
        var (items, total) = await _repository.GetStockAsync(query, SparePartStockScope.Valuation, PageOf(query), cancellationToken);
        return new SparePartValuationReportDto
        {
            Summary = summary,
            Page = PagedResult<SparePartStockItemDto>.Create(WithLedgerCheck(items), query.PageNumber, query.PageSize, total),
        };
    }

    public async Task<PagedResult<SparePartMovementItemDto>> GetMovementsAsync(SparePartMovementReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (items, total) = await _repository.GetMovementsAsync(query, PageOf(query), cancellationToken);
        return PagedResult<SparePartMovementItemDto>.Create(items, query.PageNumber, query.PageSize, total);
    }

    public async Task<PagedResult<SparePartUsageItemDto>> GetUsageAsync(SparePartUsageReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (items, total) = await _repository.GetUsageAsync(query, PageOf(query), cancellationToken);
        return PagedResult<SparePartUsageItemDto>.Create(items, query.PageNumber, query.PageSize, total);
    }

    public Task<SparePartReportLookupsDto> GetLookupsAsync(CancellationToken cancellationToken) =>
        _repository.GetLookupsAsync(cancellationToken);

    // ================================================================ Export (whole filtered, sorted set - CSV)

    public Task<ReportFile> ExportStockAsync(SparePartStockReportQuery query, CancellationToken cancellationToken) =>
        ExportPartsAsync(query, SparePartStockScope.Stock, "spare-part-stock-report", cancellationToken);

    public Task<ReportFile> ExportLowStockAsync(SparePartStockReportQuery query, CancellationToken cancellationToken) =>
        ExportPartsAsync(query, SparePartStockScope.LowStock, "spare-part-low-stock-report", cancellationToken);

    public Task<ReportFile> ExportValuationAsync(SparePartStockReportQuery query, CancellationToken cancellationToken) =>
        ExportPartsAsync(query, SparePartStockScope.Valuation, "spare-part-stock-valuation-report", cancellationToken);

    private async Task<ReportFile> ExportPartsAsync(SparePartStockReportQuery query, SparePartStockScope scope, string name, CancellationToken cancellationToken)
    {
        Validate(query, scope);
        var (items, _) = await _repository.GetStockAsync(query, scope, page: null, cancellationToken);

        var writer = new CsvReportWriter<SparePartStockItemDto>()
            .Column("Spare Part Code", r => r.SparePartCode)
            .Column("Spare Part Name", r => r.SparePartName)
            .Column("Unit", r => r.Unit)
            .Column("Current Stock", r => Int(r.CurrentStock));

        writer = scope switch
        {
            SparePartStockScope.Valuation => writer
                .Column("Unit Cost", r => Money(r.UnitCost))
                .Column("Stock Value", r => Money(r.StockValue))
                .Column("Stock Status", r => r.StockStatus),
            SparePartStockScope.LowStock => writer
                .Column("Minimum Stock", r => Int(r.MinimumStock))
                .Column("Stock Status", r => r.StockStatus)
                .Column("Unit Cost", r => Money(r.UnitCost))
                .Column("Vendor", r => r.VendorName)
                .Column("Machine", r => Code(r.MachineCode, r.MachineName))
                .Column("Active Status", r => r.IsActive ? "Active" : "Inactive"),
            _ => writer
                .Column("Minimum Stock", r => Int(r.MinimumStock))
                .Column("Stock Status", r => r.StockStatus)
                .Column("Unit Cost", r => Money(r.UnitCost))
                .Column("Category", r => r.Category)
                .Column("Part Number", r => r.PartNumber)
                .Column("Machine", r => Code(r.MachineCode, r.MachineName))
                .Column("Vendor", r => r.VendorName)
                .Column("Store Location", r => r.StoreLocation)
                .Column("Active Status", r => r.IsActive ? "Active" : "Inactive")
                .Column("Ledger Stock", r => r.LedgerStock is { } l ? Int(l) : null)
                .Column("Ledger Check", r => r.LedgerCheck),
        };

        return File(name, writer.Write(WithLedgerCheck(items)));
    }

    public async Task<ReportFile> ExportMovementsAsync(SparePartMovementReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (items, _) = await _repository.GetMovementsAsync(query, page: null, cancellationToken);

        var csv = new CsvReportWriter<SparePartMovementItemDto>()
            .Column("Transaction Date", r => CsvReportWriter<object>.DateTime(r.TransactionAt))
            .Column("Spare Part Code", r => r.SparePartCode)
            .Column("Spare Part Name", r => r.SparePartName)
            .Column("Transaction Type", r => r.TransactionType)
            .Column("Direction", r => r.Direction)
            .Column("Quantity", r => Int(r.Quantity))
            .Column("Unit", r => r.Unit)
            .Column("Previous Stock", r => Int(r.PreviousStock))
            .Column("New Stock", r => Int(r.NewStock))
            .Column("Reference", r => r.ReferenceNo)
            .Column("Created By", r => r.CreatedByName)
            .Column("Remarks", r => r.Remarks)
            .Write(items);

        return File("spare-part-stock-movement-report", csv);
    }

    public async Task<ReportFile> ExportUsageAsync(SparePartUsageReportQuery query, CancellationToken cancellationToken)
    {
        Validate(query);
        var (items, _) = await _repository.GetUsageAsync(query, page: null, cancellationToken);

        var csv = new CsvReportWriter<SparePartUsageItemDto>()
            .Column("Usage No", r => r.UsageNo)
            .Column("Usage Date", r => CsvReportWriter<object>.Date(r.UsageDate))
            .Column("Spare Part Code", r => r.SparePartCode)
            .Column("Spare Part Name", r => r.SparePartName)
            .Column("Quantity", r => Int(r.Quantity))
            .Column("Unit", r => r.Unit)
            .Column("Unit Cost at Issue", r => Money(r.UnitCostAtIssue))
            .Column("Total Cost", r => Money(r.TotalCost))
            .Column("Used For", r => r.UsedFor)
            .Column("Machine", r => Code(r.MachineCode, r.MachineName))
            .Column("Mold", r => Code(r.MoldCode, r.MoldName))
            .Column("Machine PM", r => r.MachinePmNo)
            .Column("Mold PM", r => r.MoldPmNo)
            .Column("Used By", r => r.UsedByName)
            .Column("Status", r => r.Status)
            .Column("Created At", r => CsvReportWriter<object>.DateTime(r.CreatedAt))
            .Column("Created By", r => r.CreatedByName)
            .Column("Reversed At", r => CsvReportWriter<object>.DateTime(r.ReversedAt))
            .Column("Reversal Reason", r => r.ReversalReason)
            .Column("Remarks", r => r.Remarks)
            .Write(items);

        return File("spare-part-usage-report", csv);
    }

    // ================================================================ Validation

    private static void Validate(SparePartStockReportQuery query, SparePartStockScope scope)
    {
        var errors = new List<string>();
        query.StockStatus = Canonical(query.StockStatus,
            scope == SparePartStockScope.LowStock ? SparePartReportLists.AttentionStatuses : SparePartStockStatus.All, "StockStatus", errors);
        query.Category = Trimmed(query.Category);
        query.SortBy = Canonical(query.SortBy,
            scope == SparePartStockScope.Valuation ? SparePartReportSort.ValuationSorts : SparePartReportSort.StockSorts, "SortBy", errors);
        query.SortDirection = Canonical(query.SortDirection, ReportSortDirection.All, "SortDirection", errors);
        ThrowIfAny(errors);
    }

    private static void Validate(SparePartMovementReportQuery query)
    {
        var errors = new List<string>();
        query.TransactionType = Canonical(query.TransactionType, SparePartReportLists.TransactionTypes, "TransactionType", errors);
        query.Direction = Canonical(query.Direction, SparePartMovementDirection.All, "Direction", errors);
        query.SortBy = Canonical(query.SortBy, SparePartReportSort.MovementSorts, "SortBy", errors);
        query.SortDirection = Canonical(query.SortDirection, ReportSortDirection.All, "SortDirection", errors);
        CheckRange(query.FromDate, query.ToDate, "FromDate", "ToDate", errors);
        ThrowIfAny(errors);
    }

    private static void Validate(SparePartUsageReportQuery query)
    {
        var errors = new List<string>();
        query.UsedFor = Canonical(query.UsedFor, SparePartReportLists.UsedFor, "UsedFor", errors);
        query.Status = Canonical(query.Status, SparePartReportLists.UsageStatuses, "Status", errors);
        query.SortBy = Canonical(query.SortBy, SparePartReportSort.UsageSorts, "SortBy", errors);
        query.SortDirection = Canonical(query.SortDirection, ReportSortDirection.All, "SortDirection", errors);
        CheckRange(query.FromDate, query.ToDate, "FromDate", "ToDate", errors);
        ThrowIfAny(errors);
    }

    // ================================================================ helpers

    private static List<SparePartStockItemDto> WithLedgerCheck(IEnumerable<SparePartStockItemDto> items) =>
        items.Select(i => i with { LedgerCheck = SparePartReportQueryBuilder.LedgerCheckOf(i.CurrentStock, i.LedgerStock) }).ToList();

    private static string? Money(decimal? value) => value?.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture);
    private static string Int(int value) => CsvReportWriter<object>.Number(value);
    private static string? Code(string? code, string? name) => code is null ? null : $"{code} - {name}";

    private static ReportPage PageOf(PaginationRequest query) => new(query.PageNumber, query.PageSize);

    private ReportFile File(string name, byte[] content) =>
        new($"{name}-{_dateTimeProvider.Today:yyyyMMdd}.csv", CsvReportWriter<object>.ContentType, content);
}
