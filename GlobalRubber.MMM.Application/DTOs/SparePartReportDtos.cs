using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// One spare part in the Stock, Low Stock and Valuation reports (masters.spare_part_master). StockStatus is the persisted
/// computed column; StockValue = current stock x CURRENT unit cost (null when no unit cost is set). LedgerStock is the
/// latest stock-ledger row's new stock, so a difference from the master is reported, never corrected.
/// </summary>
public sealed record SparePartStockItemDto
{
    public int SparePartId { get; init; }
    public string SparePartCode { get; init; } = string.Empty;
    public string SparePartName { get; init; } = string.Empty;
    public string? Category { get; init; }
    public string? PartNumber { get; init; }
    public string? MachineCode { get; init; }
    public string? MachineName { get; init; }
    public string? VendorName { get; init; }
    public string Unit { get; init; } = string.Empty;
    public string? StoreLocation { get; init; }
    public int CurrentStock { get; init; }
    public int MinimumStock { get; init; }
    public decimal? UnitCost { get; init; }
    public decimal? StockValue { get; init; }
    public string StockStatus { get; init; } = string.Empty;
    public bool IsActive { get; init; }
    public int? LedgerStock { get; init; }

    /// <summary>Matches / Mismatch / No Ledger (SparePartLedgerCheck).</summary>
    public string LedgerCheck { get; init; } = string.Empty;
}

/// <summary>Low Stock counts over the whole filtered set.</summary>
public sealed class SparePartLowStockSummaryDto
{
    public int TotalAttention { get; init; }
    public int LowStockCount { get; init; }
    public int OutOfStockCount { get; init; }
}

public sealed class SparePartLowStockReportDto
{
    public SparePartLowStockSummaryDto Summary { get; init; } = new();
    public PagedResult<SparePartStockItemDto> Page { get; init; } = PagedResult<SparePartStockItemDto>.Empty(1, PaginationDefaults.DefaultPageSize);
}

/// <summary>
/// Stock Valuation totals over the whole filtered set: current stock x CURRENT master unit cost (no FIFO/LIFO/average -
/// the project has no costing rule). Parts without a unit cost are counted separately and add nothing to the value.
/// </summary>
public sealed class SparePartValuationSummaryDto
{
    public int TotalParts { get; init; }
    public int PartsWithoutUnitCost { get; init; }
    public decimal TotalValue { get; init; }

    /// <summary>Value of the parts in Low Stock / Out of Stock.</summary>
    public decimal LowStockValue { get; init; }
}

public sealed class SparePartValuationReportDto
{
    public SparePartValuationSummaryDto Summary { get; init; } = new();
    public PagedResult<SparePartStockItemDto> Page { get; init; } = PagedResult<SparePartStockItemDto>.Empty(1, PaginationDefaults.DefaultPageSize);
}

/// <summary>One stock-ledger row (transactions.spare_part_stock_transaction). TransactionAt is plant (IST) time.</summary>
public sealed class SparePartMovementItemDto
{
    public long StockTransactionId { get; init; }
    public DateTime TransactionAt { get; init; }
    public string SparePartCode { get; init; } = string.Empty;
    public string SparePartName { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public string TransactionType { get; init; } = string.Empty;

    /// <summary>In / Out from the signed quantity; null for a zero-quantity row.</summary>
    public string? Direction { get; init; }

    /// <summary>Signed: + in, - out.</summary>
    public int Quantity { get; init; }
    public int PreviousStock { get; init; }
    public int NewStock { get; init; }

    /// <summary>What caused it: SparePartUsage (usage no + PM) or SparePart (master).</summary>
    public string? ReferenceType { get; init; }
    public string? ReferenceNo { get; init; }
    public string? CreatedByName { get; init; }
    public string? Remarks { get; init; }
}

/// <summary>
/// One spare part usage (transactions.spare_part_usage_transaction). TotalCost = quantity x unit_cost_at_issue (the cost
/// snapshotted at issue - never today's master cost); null when no cost was set at issue. Times are plant (IST).
/// </summary>
public sealed class SparePartUsageItemDto
{
    public int SparePartUsageId { get; init; }
    public string UsageNo { get; init; } = string.Empty;
    public DateOnly UsageDate { get; init; }
    public string SparePartCode { get; init; } = string.Empty;
    public string SparePartName { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public int Quantity { get; init; }
    public decimal? UnitCostAtIssue { get; init; }
    public decimal? TotalCost { get; init; }
    public string UsedFor { get; init; } = string.Empty;
    public string? MachineCode { get; init; }
    public string? MachineName { get; init; }
    public string? MoldCode { get; init; }
    public string? MoldName { get; init; }
    public string? MachinePmNo { get; init; }
    public string? MoldPmNo { get; init; }
    public string? UsedByName { get; init; }

    /// <summary>Posted / Reversed.</summary>
    public string Status { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
    public string? CreatedByName { get; init; }
    public DateTime? ReversedAt { get; init; }
    public string? ReversalReason { get; init; }
    public string? Remarks { get; init; }
}

/// <summary>Filter options for the Spare Part Reports, under RPT_SPARE_PART View. Fixed lists mirror the database CHECKs.</summary>
public sealed class SparePartReportLookupsDto
{
    public IReadOnlyList<ReportLookupItemDto> SpareParts { get; init; } = Array.Empty<ReportLookupItemDto>();

    /// <summary>Machines linked to a spare part or used by a spare part usage.</summary>
    public IReadOnlyList<ReportLookupItemDto> Machines { get; init; } = Array.Empty<ReportLookupItemDto>();

    /// <summary>Molds used by a spare part usage.</summary>
    public IReadOnlyList<ReportLookupItemDto> Molds { get; init; } = Array.Empty<ReportLookupItemDto>();

    /// <summary>Vendors linked to a spare part.</summary>
    public IReadOnlyList<ReportLookupItemDto> Vendors { get; init; } = Array.Empty<ReportLookupItemDto>();

    /// <summary>Distinct categories in use.</summary>
    public IReadOnlyList<string> Categories { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> StockStatuses { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> AttentionStatuses { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> TransactionTypes { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Directions { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UsageStatuses { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> UsedFor { get; init; } = Array.Empty<string>();
}
