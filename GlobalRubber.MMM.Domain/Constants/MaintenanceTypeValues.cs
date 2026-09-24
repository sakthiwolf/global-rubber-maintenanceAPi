namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>
/// masters.maintenance_type_master.applies_to values, exactly as CK_maintenance_type_master_applies_to allows them
/// (analysis 4.8: Machine / Mold / Both).
/// </summary>
public static class MaintenanceTypeAppliesTo
{
    public const string Machine = "Machine";
    public const string Mold = "Mold";
    public const string Both = "Both";

    public static readonly IReadOnlyList<string> All = new[] { Machine, Mold, Both };
}
