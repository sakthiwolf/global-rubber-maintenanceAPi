using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Domain.Rules;

/// <summary>
/// What Machine PM does to the machine: an operational status of Maintenance becomes Running on completion, any other
/// status is left alone (analysis BR-23); the next maintenance date is the earliest OPEN occurrence (approved Q6 / C3,
/// 2026-09-25 - it replaced the template's "completion + frequency days" rule).
/// </summary>
public static class MachinePmRules
{
    /// <summary>
    /// Recurring PM (approved Q6 / C3, 2026-09-25): a machine's next maintenance date is the earliest due date among its
    /// OPEN occurrences (Scheduled / In Progress); with none it is NULL (the column's existing "no next date" value).
    /// </summary>
    public static DateOnly? NextMaintenanceDateFromOpenOccurrences(IEnumerable<DateOnly> openDueDates)
    {
        var dates = openDueDates.ToList();
        return dates.Count == 0 ? null : dates.Min();
    }

    public static string OperationalStatusAfterCompletion(string currentStatus) =>
        currentStatus == MachineOperationalStatus.Maintenance ? MachineOperationalStatus.Running : currentStatus;
}
