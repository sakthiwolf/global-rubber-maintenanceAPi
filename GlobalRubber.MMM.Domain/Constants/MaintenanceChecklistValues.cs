namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>
/// masters.maintenance_checklist_master.applies_to values, exactly as CK_maintenance_checklist_master_applies_to allows
/// them (analysis 4.10: Machine / Mold - unlike Maintenance Type there is no "Both").
/// </summary>
public static class MaintenanceChecklistAppliesTo
{
    public const string Machine = "Machine";
    public const string Mold = "Mold";

    public static readonly IReadOnlyList<string> All = new[] { Machine, Mold };
}

/// <summary>
/// masters.maintenance_checklist_master.frequency values, exactly as CK_maintenance_checklist_master_frequency allows
/// them (migration 012). Required for every ACTIVE checklist (CK_maintenance_checklist_master_active_config).
/// </summary>
public static class ChecklistFrequency
{
    public const string Daily = "Daily";
    public const string Weekly = "Weekly";
    public const string Monthly = "Monthly";
    public const string Yearly = "Yearly";

    public static readonly IReadOnlyList<string> All = new[] { Daily, Weekly, Monthly, Yearly };
}
