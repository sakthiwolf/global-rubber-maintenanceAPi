using GlobalRubber.MMM.Domain.Common;
using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Domain.Rules;

/// <summary>
/// What a machine breakdown does to its machine's operational status (analysis BR-16 / BR-20, §6.2):
/// reporting a breakdown sets the machine to Breakdown; resolving it sets the machine back to Running. A machine can have
/// several breakdowns open at once (nothing prevents it), so a resolution only returns the machine to Running when none of
/// its OTHER breakdowns is still unresolved, and - like Machine PM, which only turns its own Maintenance status back to
/// Running (BR-23) - only when the machine is still in the Breakdown status this module set.
/// The last maintenance date is NOT touched (open question Q-20).
/// </summary>
public static class MachineBreakdownRules
{
    /// <summary>Stages in which the breakdown still keeps the machine down (before Resolved).</summary>
    public static readonly IReadOnlyList<string> UnresolvedStages =
        new[] { BreakdownStage.Reported, BreakdownStage.Assigned, BreakdownStage.MaintenanceStarted };

    /// <summary>
    /// Downtime: from the moment the machine broke down (the reported breakdown date + time, plant / IST local) to the
    /// resolution, in hours rounded to 0.1 - the whole time the machine was down, including the wait before maintenance
    /// started. Never negative (a breakdown time entered after the resolution counts as 0).
    /// </summary>
    public static decimal DowntimeHours(DateOnly breakdownDate, TimeOnly breakdownTime, DateTime resolvedAtUtc) =>
        HoursBetween(PlantTime.ToUtc(breakdownDate, breakdownTime), resolvedAtUtc);

    /// <summary>Repair time: maintenance started to resolved (both UTC), hours rounded to 0.1, never negative.</summary>
    public static decimal? RepairHours(DateTime? maintenanceStartedAtUtc, DateTime? resolvedAtUtc) =>
        maintenanceStartedAtUtc is { } start && resolvedAtUtc is { } end ? HoursBetween(start, end) : null;

    private static decimal HoursBetween(DateTime fromUtc, DateTime toUtc) =>
        Math.Max(0m, Math.Round((decimal)(toUtc - fromUtc).TotalHours, 1, MidpointRounding.AwayFromZero));

    /// <summary>BR-16: a reported breakdown puts the machine into Breakdown.</summary>
    public static string OperationalStatusAfterReport(string currentStatus) => MachineOperationalStatus.Breakdown;

    /// <summary>BR-20: Breakdown becomes Running when no other breakdown of the machine is still unresolved.</summary>
    public static string OperationalStatusAfterResolve(string currentStatus, int otherUnresolvedBreakdowns) =>
        currentStatus == MachineOperationalStatus.Breakdown && otherUnresolvedBreakdowns == 0
            ? MachineOperationalStatus.Running
            : currentStatus;
}
