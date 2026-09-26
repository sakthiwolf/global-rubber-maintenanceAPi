namespace GlobalRubber.MMM.Domain.Constants;

/// <summary>
/// transactions.machine_pm_transaction.status values, exactly as CK_machine_pm_transaction_status allows them (default
/// 'Scheduled'). 'In Progress' exists in the database but nothing sets it: the template's "Start Maintenance" completes
/// directly (analysis 6.3, D-17, Q-18). Overdue is derived from the date, never stored (D-03).
/// </summary>
public static class MachinePmStatus
{
    public const string Scheduled = "Scheduled";
    public const string InProgress = "In Progress";
    public const string Completed = "Completed";

    public static readonly IReadOnlyList<string> All = new[] { Scheduled, InProgress, Completed };
}

/// <summary>
/// The Machine PM page's tabs (user decision 2026-09-25, replacing the calendar tabs today/week/overdue/upcoming): the
/// FREQUENCY of the PM's checklist is the tab, the status is only the PM's state.
///   daily / weekly / monthly / yearly - DUE work: not completed, checklist frequency = that frequency, and
///                                       scheduled date &lt;= today (plant/IST date). A successor created at completion
///                                       for a future cycle date stays hidden until that date arrives (user decision
///                                       2026-09-25); overdue ones stay included - overdue is a badge, never a tab.
///   completed                         - status Completed, whatever the frequency
/// </summary>
public static class MachinePmBucket
{
    public const string Daily = "daily";
    public const string Weekly = "weekly";
    public const string Monthly = "monthly";
    public const string Yearly = "yearly";
    public const string Completed = "completed";

    public static readonly IReadOnlyList<string> All = new[] { Daily, Weekly, Monthly, Yearly, Completed };

    /// <summary>The checklist frequency a frequency tab stands for (ChecklistFrequency values); null for completed.</summary>
    public static string? FrequencyOf(string bucket) => bucket switch
    {
        Daily => ChecklistFrequency.Daily,
        Weekly => ChecklistFrequency.Weekly,
        Monthly => ChecklistFrequency.Monthly,
        Yearly => ChecklistFrequency.Yearly,
        _ => null,
    };
}
