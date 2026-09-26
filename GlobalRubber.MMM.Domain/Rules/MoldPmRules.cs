using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Domain.Rules;

/// <summary>
/// Usage-based Mold Preventive Maintenance (approved 2026-09-25, migration 014). Pure rules - no I/O.
///
/// The mold's cumulative usage (current_usage_shots) is the source of truth. The PM interval is maintenance_frequency_shots
/// (NULL = disabled). The current cycle starts at pm_cycle_start_shots; its threshold is cycle start + interval:
///   remaining = threshold - usage
///   Warning   when 0 &lt; remaining &lt;= warning margin (pm_warning_shots)
///   Due       when usage &gt;= threshold  -&gt; ONE automatic PM for the cycle.
/// Completion re-anchors the cycle on the completed PM's own threshold, in whole intervals, so late maintenance never shifts
/// the cycle and missed cycles create no backlog: due at 600,000, completed at 615,000 -&gt; cycle start 600,000, next
/// threshold 700,000; completed at 720,000 -&gt; next 800,000.
/// </summary>
public static class MoldPmRules
{
    /// <summary>Usage-based PM is configured for the mold (an interval above 0).</summary>
    public static bool IsEnabled(int? intervalShots) => intervalShots is > 0;

    /// <summary>The threshold of the current cycle: cycle start + interval.</summary>
    public static long NextThreshold(int cycleStartShots, int intervalShots) => (long)cycleStartShots + intervalShots;

    /// <summary>Shots left until the threshold; 0 or less once it is reached (negative = shots past it).</summary>
    public static long RemainingShots(int usageShots, int cycleStartShots, int intervalShots) =>
        NextThreshold(cycleStartShots, intervalShots) - usageShots;

    public static bool IsDue(int usageShots, int cycleStartShots, int intervalShots) =>
        usageShots >= NextThreshold(cycleStartShots, intervalShots);

    /// <summary>Remaining shots are above 0 but no more than the warning margin (no margin = never a warning).</summary>
    public static bool IsWarning(int usageShots, int cycleStartShots, int intervalShots, int? warningShots)
    {
        var remaining = RemainingShots(usageShots, cycleStartShots, intervalShots);
        return warningShots is > 0 && remaining > 0 && remaining <= warningShots;
    }

    /// <summary>
    /// The cycle start after completing the PM that was due at <paramref name="thresholdShots"/>: that threshold plus every
    /// WHOLE interval the usage has already passed. Never below the PM's threshold, so a cycle is never repeated (even if the
    /// usage was corrected down while the PM was open).
    /// </summary>
    public static int CycleStartAfterCompletion(int thresholdShots, int intervalShots, int usageAtCompletion)
    {
        if (intervalShots <= 0) throw new ArgumentOutOfRangeException(nameof(intervalShots), "The interval must be greater than 0.");
        if (usageAtCompletion <= thresholdShots) return thresholdShots;

        var wholeIntervals = ((long)usageAtCompletion - thresholdShots) / intervalShots;
        return checked((int)(thresholdShots + wholeIntervals * intervalShots));
    }

    /// <summary>What an evaluation of the (locked) mold must do. At most one of the two is true.</summary>
    public sealed record Decision(bool CreatePm, bool RaiseWarning, long ThresholdShots, long RemainingShots)
    {
        public static readonly Decision None = new(false, false, 0, 0);
    }

    /// <summary>
    /// The evaluation after the mold's usage or PM configuration changed. Nothing happens for a disabled or Retired mold,
    /// or while a PM is still open (one open PM, no backlog). Due -&gt; create the cycle's PM; otherwise Warning -&gt; raise the
    /// cycle's warning. Whether that warning was already raised is the caller's (persisted, unique) concern.
    /// </summary>
    public static Decision Decide(int? intervalShots, int cycleStartShots, int? warningShots, int usageShots, string moldStatus, bool hasOpenPm)
    {
        if (!IsEnabled(intervalShots) || moldStatus == MoldStatus.Retired || hasOpenPm)
        {
            return Decision.None;
        }

        var interval = intervalShots!.Value;
        var threshold = NextThreshold(cycleStartShots, interval);
        var remaining = threshold - usageShots;

        if (usageShots >= threshold)
        {
            return new Decision(CreatePm: true, RaiseWarning: false, threshold, remaining);
        }

        return IsWarning(usageShots, cycleStartShots, interval, warningShots)
            ? new Decision(CreatePm: false, RaiseWarning: true, threshold, remaining)
            : Decision.None;
    }

    /// <summary>
    /// The mold's PM state for display (derived, never stored). An open PM decides first: In Progress -&gt; In Maintenance;
    /// Scheduled -&gt; Overdue when it became due before today (IST), else Due. Without one: Due / Warning / Normal from the
    /// usage (a Retired mold is evaluated the same way but never gets a PM).
    /// </summary>
    public static string StateOf(
        int? intervalShots, int cycleStartShots, int? warningShots, int usageShots,
        string? openPmStatus, DateOnly? openPmScheduledDate, DateOnly today)
    {
        if (openPmStatus == MoldPmStatus.InProgress) return MoldPmState.InMaintenance;
        if (openPmStatus == MoldPmStatus.Scheduled) return IsOverdue(openPmScheduledDate, today) ? MoldPmState.Overdue : MoldPmState.Due;
        if (!IsEnabled(intervalShots)) return MoldPmState.NotConfigured;

        var interval = intervalShots!.Value;
        if (IsDue(usageShots, cycleStartShots, interval)) return MoldPmState.Due;
        return IsWarning(usageShots, cycleStartShots, interval, warningShots) ? MoldPmState.Warning : MoldPmState.Normal;
    }

    /// <summary>A Scheduled (not started) PM is overdue once the day it became due is in the past - as Machine PM's overdue.</summary>
    public static bool IsOverdue(DateOnly? scheduledDate, DateOnly today) => scheduledDate is { } d && d < today;
}
