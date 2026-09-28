namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>
/// Direction of a stock-ledger row, read from its SIGNED quantity (the ledger stores + for stock in, - for stock out;
/// CK: new = previous + quantity). Presentation only - nothing new is stored or decided.
/// </summary>
public static class SparePartMovementDirection
{
    public const string In = "In";
    public const string Out = "Out";

    public static readonly IReadOnlyList<string> All = new[] { In, Out };
}

/// <summary>Values the Spare Part Reports accept for the fixed database lists they filter on.</summary>
public static class SparePartReportLists
{
    /// <summary>CK_spare_part_stock_transaction_type (migration 016).</summary>
    public static readonly IReadOnlyList<string> TransactionTypes = new[]
    {
        SparePartStockTransactionType.Opening, SparePartStockTransactionType.Issue,
        SparePartStockTransactionType.Reversal, SparePartStockTransactionType.Adjustment,
    };

    /// <summary>CK_spare_part_usage_transaction_status.</summary>
    public static readonly IReadOnlyList<string> UsageStatuses = new[] { SparePartUsageStatus.Posted, SparePartUsageStatus.Reversed };

    /// <summary>
    /// CK_spare_part_usage_transaction_used_for (database/scripts/008). The Spare Part Usage screen only records Machine /
    /// Mold Maintenance; the other values are allowed by the table and filterable, but no workflow writes them.
    /// </summary>
    public static readonly IReadOnlyList<string> UsedFor = new[]
    {
        "Machine Breakdown", SparePartUsageMaintenanceType.UsedForMachineMaintenance, SparePartUsageMaintenanceType.UsedForMoldMaintenance,
        "Work Order", "Other",
    };

    /// <summary>Stock statuses that need attention (the Low Stock report).</summary>
    public static readonly IReadOnlyList<string> AttentionStatuses = new[] { SparePartStockStatus.LowStock, SparePartStockStatus.OutOfStock };
}

/// <summary>Whether a spare part's master stock agrees with its stock ledger (the latest ledger row's new stock).</summary>
public static class SparePartLedgerCheck
{
    public const string Matches = "Matches";
    public const string Mismatch = "Mismatch";

    /// <summary>The part has no ledger row at all.</summary>
    public const string NoLedger = "No Ledger";
}

/// <summary>Sort keys of the Spare Part Reports.</summary>
public static class SparePartReportSort
{
    public const string Code = "code";
    public const string Name = "name";
    public const string CurrentStock = "currentStock";
    public const string MinimumStock = "minimumStock";
    public const string UnitCost = "unitCost";
    public const string StockStatus = "stockStatus";
    public const string StockValue = "stockValue";

    public const string Date = "date";
    public const string SparePart = "sparePart";
    public const string Quantity = "quantity";
    public const string Type = "type";
    public const string NewStock = "newStock";
    public const string TotalCost = "totalCost";
    public const string Status = "status";

    /// <summary>Spare Part Stock and Low Stock.</summary>
    public static readonly IReadOnlyList<string> StockSorts = new[] { Code, Name, CurrentStock, MinimumStock, UnitCost, StockStatus };

    /// <summary>Stock Valuation.</summary>
    public static readonly IReadOnlyList<string> ValuationSorts = new[] { Code, Name, CurrentStock, UnitCost, StockValue, StockStatus };

    public static readonly IReadOnlyList<string> MovementSorts = new[] { Date, SparePart, Quantity, Type, NewStock };

    public static readonly IReadOnlyList<string> UsageSorts = new[] { Date, SparePart, Quantity, TotalCost, Status };
}
