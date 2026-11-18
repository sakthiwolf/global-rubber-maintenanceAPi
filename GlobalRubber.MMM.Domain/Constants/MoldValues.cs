namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>
/// masters.mold_master.status values, exactly as CK_mold_master_status allows them (default 'Available',
/// DF_mold_master_status). The mold table has no is_active column: 'Retired' plays that role (see
/// database/scripts/005_create_master_tables.sql). Approved 2026-11-18 (A2, replacing Q-09): the status is system-managed
/// (MoldLifeEngine.StatusAfterMasterSave / StatusAfterProduction, Mold PM start/complete); only Retire / Reactivate is manual.
/// </summary>
public static class MoldStatus
{
    public const string Available = "Available";
    public const string InProduction = "In Production";
    public const string Maintenance = "Maintenance";
    public const string ReplacementDue = "Replacement Due";
    public const string Retired = "Retired";

    public static readonly IReadOnlyList<string> All = new[] { Available, InProduction, Maintenance, ReplacementDue, Retired };
}

/// <summary>masters.mold_master.life_state values - the persisted computed column (analysis section 8.2).</summary>
public static class MoldLifeState
{
    public const string Normal = "Normal";
    public const string Warning = "Warning";
    public const string Replace = "Replace";

    public static readonly IReadOnlyList<string> All = new[] { Normal, Warning, Replace };
}
