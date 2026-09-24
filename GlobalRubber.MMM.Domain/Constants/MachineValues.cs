namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>
/// masters.machine_master.criticality values, exactly as CK_machine_master_criticality allows them
/// (database/scripts/008_create_constraints.sql). Default 'Medium' (DF_machine_master_criticality).
/// </summary>
public static class MachineCriticality
{
    public const string Low = "Low";
    public const string Medium = "Medium";
    public const string High = "High";

    public static readonly IReadOnlyList<string> All = new[] { Low, Medium, High };
}

/// <summary>
/// masters.machine_master.operational_status values, exactly as CK_machine_master_operational_status allows them.
/// System-managed: a new machine is Running (BR-05); afterwards only transactions change it (analysis 4.1, Q-19) - it is
/// never accepted from a Machine create/update request.
/// </summary>
public static class MachineOperationalStatus
{
    public const string Running = "Running";
    public const string Idle = "Idle";
    public const string Breakdown = "Breakdown";
    public const string Maintenance = "Maintenance";

    public static readonly IReadOnlyList<string> All = new[] { Running, Idle, Breakdown, Maintenance };
}
