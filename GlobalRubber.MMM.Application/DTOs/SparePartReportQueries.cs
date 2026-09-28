using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Query string of the master-based Spare Part Reports: GET /api/v1/reports/spare-parts (stock), .../low-stock and
/// .../valuation (and their exports). Low Stock accepts only Low Stock / Out of Stock as StockStatus.
/// </summary>
public sealed class SparePartStockReportQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on code, name, category, part number or store location.</summary>
    public string? Search { get; set; }

    /// <summary>true = active parts, false = inactive, omitted = both.</summary>
    public bool? IsActive { get; set; }

    /// <summary>Available / Low Stock / Out of Stock (the persisted computed stock_status, BR-29).</summary>
    public string? StockStatus { get; set; }

    public int? VendorId { get; set; }
    public int? MachineId { get; set; }

    /// <summary>Exact category (values from GET .../lookups).</summary>
    public string? Category { get; set; }

    /// <summary>Stock / Low Stock: SparePartReportSort.StockSorts; Valuation: ValuationSorts. Default code.</summary>
    public string? SortBy { get; set; }

    /// <summary>asc / desc (default: code/name asc, figures desc).</summary>
    public string? SortDirection { get; set; }
}

/// <summary>
/// Query string of GET /api/v1/reports/spare-parts/movements (and .../export) - the stock ledger. The date range is the
/// plant (IST) date of the movement.
/// </summary>
public sealed class SparePartMovementReportQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on part code/name, reference no or remarks.</summary>
    public string? Search { get; set; }

    public int? SparePartId { get; set; }

    /// <summary>Opening / Issue / Reversal / Adjustment.</summary>
    public string? TransactionType { get; set; }

    /// <summary>In (quantity &gt; 0) / Out (quantity &lt; 0).</summary>
    public string? Direction { get; set; }

    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    /// <summary>date (default, newest first) / sparePart / quantity / type / newStock.</summary>
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }
}

/// <summary>Query string of GET /api/v1/reports/spare-parts/usage (and .../export). The date range is the usage date.</summary>
public sealed class SparePartUsageReportQuery : PaginationRequest
{
    /// <summary>Case-insensitive "contains" match on usage no, part code/name, machine/mold code, PM no, used by or remarks.</summary>
    public string? Search { get; set; }

    public int? SparePartId { get; set; }
    public int? MachineId { get; set; }
    public int? MoldId { get; set; }

    /// <summary>Used For (CK_spare_part_usage_transaction_used_for), e.g. Machine Maintenance / Mold Maintenance.</summary>
    public string? UsedFor { get; set; }

    /// <summary>Posted / Reversed.</summary>
    public string? Status { get; set; }

    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }

    /// <summary>date (default, newest first) / sparePart / quantity / totalCost / status.</summary>
    public string? SortBy { get; set; }
    public string? SortDirection { get; set; }
}
