namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>
/// The maintenance records the Maintenance Reports cover: the two PM workflows only. Breakdowns are corrective
/// maintenance with their own reports (analysis R-03/R-14) and work orders are a deferred module (no rows), so neither is
/// a maintenance-report source (analysis 9.2: R-10..R-13 are PM reports).
/// </summary>
public static class MaintenanceReportAssetType
{
    public const string Machine = "Machine";
    public const string Mold = "Mold";

    public static readonly IReadOnlyList<string> All = new[] { Machine, Mold };
}

/// <summary>The source label of a maintenance-report row (one per PM workflow).</summary>
public static class MaintenanceReportSource
{
    public const string MachinePm = "Machine PM";
    public const string MoldPm = "Mold PM";
}

/// <summary>
/// The PM status shown by the Maintenance Reports - each workflow keeps its OWN page's terminology (never a new rule):
///   Machine PM (MachinePmService): Completed; Overdue when not completed and due before today (IST, D-03); otherwise the
///              stored status (Scheduled / In Progress).
///   Mold PM (MoldPmService / MoldPmRules): Completed; In Progress; Scheduled is Overdue once the day it became due is past
///              (MoldPmRules.IsOverdue), otherwise Due.
/// </summary>
public static class MaintenanceReportStatus
{
    public const string Scheduled = "Scheduled";
    public const string Due = "Due";
    public const string Overdue = "Overdue";
    public const string InProgress = "In Progress";
    public const string Completed = "Completed";

    /// <summary>Statuses an open (not completed) PM can have - the PM Schedule tab's status filter.</summary>
    public static readonly IReadOnlyList<string> Open = new[] { Scheduled, Due, Overdue, InProgress };

    /// <summary>Every status - the Maintenance History tab's status filter.</summary>
    public static readonly IReadOnlyList<string> All = new[] { Scheduled, Due, Overdue, InProgress, Completed };
}

/// <summary>Sort keys of the Maintenance Reports.</summary>
public static class MaintenanceReportSort
{
    /// <summary>The tab's own date (schedule/overdue: due date; completed: completed date; history: maintenance date).</summary>
    public const string Date = "date";
    public const string Asset = "asset";
    public const string Source = "source";
    public const string Status = "status";

    public static readonly IReadOnlyList<string> All = new[] { Date, Asset, Source, Status };
}
