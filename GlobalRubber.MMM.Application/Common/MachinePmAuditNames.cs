namespace GlobalRubber.MMM.Application.Common;

/// <summary>
/// Audit identifiers of a Machine PM occurrence, shared by every service that writes one (the checklist service creates
/// occurrences too). Values are exactly those the Machine PM module already writes; actions fit audit_log.action VARCHAR(30).
/// </summary>
public static class MachinePmAuditNames
{
    public const string Module = "Machine Preventive Maintenance";
    public const string EntityName = "MachinePm";
    public const string Scheduled = "MachinePmScheduled";

    /// <summary>Approved 2026-11-18 (A1): a user started a machine PM (In Progress, machine under Maintenance).</summary>
    public const string Started = "MachinePmStarted";

    /// <summary>Migration 025: a user scheduled a manual (one-time) machine PM.</summary>
    public const string ManualScheduled = "MachinePmManualScheduled";
}
