namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>
/// Status filter of the Machine Reports "Maintenance History" tab. Derived from machine_pm_transaction exactly as the
/// Machine PM module derives it - nothing is stored:
///   Completed - status Completed
///   Scheduled - not completed, due today or later (plant/IST date)
///   Overdue   - not completed, due before today (plant/IST date) - the same rule as MachinePmDto.IsOverdue (D-03)
/// </summary>
public static class MachineReportPmStatus
{
    public const string Completed = "Completed";
    public const string Scheduled = "Scheduled";
    public const string Overdue = "Overdue";

    public static readonly IReadOnlyList<string> All = new[] { Completed, Scheduled, Overdue };
}

/// <summary>
/// Status of a breakdown in the Machine Reports "Breakdown" / "Downtime" tabs, derived from its stage:
///   Open     - Reported / Assigned / Maintenance Started (not yet resolved)
///   Resolved - Resolved / Closed (downtime_hours was fixed when it reached Resolved)
/// </summary>
public static class MachineReportBreakdownStatus
{
    public const string Open = "Open";
    public const string Resolved = "Resolved";

    public static readonly IReadOnlyList<string> All = new[] { Open, Resolved };
}

/// <summary>
/// Sort keys of the breakdown report rows - Machine Reports "Breakdown" / "Downtime" tabs and the Breakdown Reports
/// (default: date, newest first). status = Open before Resolved, then by stage (Reported ... Closed).
/// </summary>
public static class MachineReportBreakdownSort
{
    public const string Date = "date";
    public const string Machine = "machine";
    public const string Duration = "duration";
    public const string Priority = "priority";
    public const string Status = "status";

    public static readonly IReadOnlyList<string> All = new[] { Date, Machine, Duration, Priority, Status };
}

/// <summary>Sort directions accepted by the report endpoints (Machine and Mold Reports).</summary>
public static class ReportSortDirection
{
    public const string Ascending = "asc";
    public const string Descending = "desc";

    public static readonly IReadOnlyList<string> All = new[] { Ascending, Descending };
}
