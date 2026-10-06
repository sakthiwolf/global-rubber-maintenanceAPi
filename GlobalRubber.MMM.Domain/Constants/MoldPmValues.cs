namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>transactions.mold_pm_transaction.status values, exactly as CK_mold_pm_transaction_status allows them.</summary>
public static class MoldPmStatus
{
    public const string Scheduled = "Scheduled";
    public const string InProgress = "In Progress";
    public const string Completed = "Completed";
}

/// <summary>transactions.mold_pm_transaction.category - the automatic usage-based PM is 'Shot-based' (CK_mold_pm_transaction_category).</summary>
public static class MoldPmCategory
{
    public const string ShotBased = "Shot-based";

    /// <summary>The category of a MANUAL (one-time) mold PM (migration 025) - already allowed by the CHECK constraint.</summary>
    public const string Scheduled = "Scheduled";

    /// <summary>Every value CK_mold_pm_transaction_category allows (database/scripts/008_create_constraints.sql).</summary>
    public static readonly IReadOnlyList<string> All = new[] { Scheduled, ShotBased, "Damage Repair", "Cleaning", "Inspection", "Preventive", "Replacement" };
}

/// <summary>
/// The Mold PM page's tabs. due = Scheduled and became due today; overdue = Scheduled and became due before today (IST);
/// in-progress = started; completed. Any other value is a 400.
/// </summary>
public static class MoldPmBucket
{
    public const string Due = "due";
    public const string Overdue = "overdue";
    public const string InProgress = "in-progress";
    public const string Completed = "completed";

    public static readonly IReadOnlyList<string> All = new[] { Due, Overdue, InProgress, Completed };
}

/// <summary>A mold's usage-based PM state - always DERIVED from the authoritative columns (see MoldPmRules.StateOf), never stored.</summary>
public static class MoldPmState
{
    /// <summary>No interval configured: usage-based PM is disabled for the mold.</summary>
    public const string NotConfigured = "Not Configured";
    public const string Normal = "Normal";
    public const string Warning = "Warning";
    public const string Due = "Due";
    public const string Overdue = "Overdue";
    public const string InMaintenance = "In Maintenance";
}

/// <summary>transactions.notification_transaction.notification_type / severity (CK constraints of migration 014).</summary>
public static class NotificationTypes
{
    public const string MoldPmWarning = "MoldPmWarning";
    public const string MoldPmDue = "MoldPmDue";

    /// <summary>A spare part's stock moved into Low Stock (migration 016).</summary>
    public const string SparePartLowStock = "SparePartLowStock";

    /// <summary>A spare part's stock moved into Out of Stock (migration 016).</summary>
    public const string SparePartOutOfStock = "SparePartOutOfStock";

    // Migration 021: the Machine Breakdown workflow (module TRN_MACHINE_BREAKDOWN) ...
    public const string BreakdownReported = "BreakdownReported";
    public const string BreakdownAssigned = "BreakdownAssigned";
    public const string BreakdownStarted = "BreakdownStarted";
    public const string BreakdownResolved = "BreakdownResolved";
    public const string BreakdownClosed = "BreakdownClosed";
    public const string BreakdownReopened = "BreakdownReopened";

    // ... and Machine PM (module TRN_MACHINE_PM): an occurrence was scheduled, its date has come, its date has passed.
    public const string MachinePmScheduled = "MachinePmScheduled";
    public const string MachinePmDue = "MachinePmDue";
    public const string MachinePmOverdue = "MachinePmOverdue";

    /// <summary>Exactly what CK_notification_transaction_type allows (migrations 014 + 016 + 021).</summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        MoldPmWarning, MoldPmDue, SparePartLowStock, SparePartOutOfStock,
        BreakdownReported, BreakdownAssigned, BreakdownStarted, BreakdownResolved, BreakdownClosed, BreakdownReopened,
        MachinePmScheduled, MachinePmDue, MachinePmOverdue,
    };
}

public static class NotificationSeverity
{
    public const string Info = "Info";
    public const string Warning = "Warning";
    public const string Critical = "Critical";

    /// <summary>Exactly what CK_notification_transaction_severity allows.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Info, Warning, Critical };
}
