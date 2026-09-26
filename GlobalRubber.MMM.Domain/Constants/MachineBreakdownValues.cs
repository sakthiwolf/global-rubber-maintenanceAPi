namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>
/// transactions.machine_breakdown_transaction.priority values, exactly as
/// CK_machine_breakdown_transaction_priority allows them
/// (database/scripts/008_create_constraints.sql). Default 'Medium'
/// (DF_machine_breakdown_transaction_priority).
/// </summary>
public static class BreakdownPriority
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";
    public const string Critical = "Critical";

    public static readonly IReadOnlyList<string> All = new[] { Low, Medium, High, Critical };
}

/// <summary>
/// transactions.machine_breakdown_transaction.stage values, exactly as
/// CK_machine_breakdown_transaction_stage allows them.
/// Stage moves forward only: Reported → Assigned → Maintenance Started → Resolved → Closed.
/// </summary>
public static class BreakdownStage
{
    public const string Reported = "Reported";
    public const string Assigned = "Assigned";
    public const string MaintenanceStarted = "Maintenance Started";
    public const string Resolved = "Resolved";
    public const string Closed = "Closed";

    public static readonly IReadOnlyList<string> All = new[] { Reported, Assigned, MaintenanceStarted, Resolved, Closed };
}

/// <summary>audit.audit_log.action values for Machine Breakdown events.</summary>
public static class BreakdownAuditNames
{
    public const string Created = "MachineBreakdownCreated";
    public const string StageChanged = "MachineBreakdownStageChanged";
}
