namespace GlobalRubber.MMM.Application.Common;

/// <summary>
/// Audit values for the automatic Mold Preventive Maintenance. Actions fit audit_log.action VARCHAR(30). The automatic
/// ones (AutoCreated, WarningRaised) are written as the system (user name "System", no user id).
/// </summary>
public static class MoldPmAuditNames
{
    public const string Module = "Mold Preventive Maintenance";
    public const string EntityName = "MoldPm";
    public const string MoldEntityName = "Mold";

    /// <summary>The system created the cycle's PM because the usage reached the threshold.</summary>
    public const string AutoCreated = "MoldPmAutoCreated";

    /// <summary>The system raised the cycle's warning notification.</summary>
    public const string WarningRaised = "MoldPmWarningRaised";

    /// <summary>Migration 025: a user scheduled a manual (one-time) mold PM.</summary>
    public const string ManualScheduled = "MoldPmManualScheduled";

    public const string Started = "MoldPmStarted";
    public const string Completed = "MoldPmCompleted";

    /// <summary>The Trigger shown for the automatic PM.</summary>
    public const string UsageThresholdTrigger = "Usage threshold";

    /// <summary>The Trigger shown for a manual (one-time) PM (migration 025).</summary>
    public const string ManualTrigger = "Manual schedule";
}
