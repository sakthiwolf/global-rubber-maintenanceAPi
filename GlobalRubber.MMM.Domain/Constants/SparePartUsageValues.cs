namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>transactions.spare_part_usage_transaction.status (CK_spare_part_usage_transaction_status, migration 016).</summary>
public static class SparePartUsageStatus
{
    public const string Posted = "Posted";
    public const string Reversed = "Reversed";
}

/// <summary>
/// The maintenance a usage is issued against, as the API names it, and the matching used_for value
/// (CK_spare_part_usage_transaction_used_for). Only Machine PM and Mold PM are supported by this module.
/// </summary>
public static class SparePartUsageMaintenanceType
{
    public const string MachinePm = "Machine PM";
    public const string MoldPm = "Mold PM";

    public static readonly IReadOnlyList<string> All = new[] { MachinePm, MoldPm };

    public const string UsedForMachineMaintenance = "Machine Maintenance";
    public const string UsedForMoldMaintenance = "Mold Maintenance";

    public static string UsedForOf(string maintenanceType) =>
        maintenanceType == MachinePm ? UsedForMachineMaintenance : UsedForMoldMaintenance;

    public static string MaintenanceTypeOf(string usedFor) =>
        usedFor == UsedForMachineMaintenance ? MachinePm : usedFor == UsedForMoldMaintenance ? MoldPm : usedFor;
}

/// <summary>transactions.spare_part_stock_transaction.transaction_type (CK_spare_part_stock_transaction_type).</summary>
public static class SparePartStockTransactionType
{
    public const string Opening = "Opening";
    public const string Issue = "Issue";
    public const string Reversal = "Reversal";
    public const string Adjustment = "Adjustment";
}

/// <summary>transactions.spare_part_stock_transaction.reference_type values.</summary>
public static class SparePartStockReferenceType
{
    public const string SparePartUsage = "SparePartUsage";
    public const string SparePart = "SparePart";
}
