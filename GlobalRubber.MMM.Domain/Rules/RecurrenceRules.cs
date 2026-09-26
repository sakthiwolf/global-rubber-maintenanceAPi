using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Domain.Rules;

/// <summary>
/// The recurring Machine PM cycle (approved 2026-09-25, decisions Q1 / Q2+Q8 / C1).
///
/// Every occurrence is computed from the checklist's permanent cycle ANCHOR (its creation date in plant time) - never by
/// stepping from the previous due date - so the cycle never drifts:
///   occurrence(n) = anchor + n days | n weeks | n months | n years
/// A month or year that lacks the anchor's day uses its last day (31-Jan -&gt; 28-Feb -&gt; 31-Mar -&gt; 30-Apr; a 29-Feb
/// anchor -&gt; 28-Feb in common years -&gt; 29-Feb again in leap years), and the next occurrence returns to the anchor's day.
///
/// Late completion never shifts the cycle and never creates a backlog: after a completion the ONE next occurrence is the
/// first cycle date strictly after both the completion date and the completed occurrence's own due date (so completing
/// early can never re-create the same occurrence).
/// </summary>
public static class RecurrenceRules
{
    /// <summary>The n-th occurrence of the cycle (n = 0 is the anchor itself).</summary>
    public static DateOnly OccurrenceAt(DateOnly anchor, string frequency, int n)
    {
        if (n < 0) throw new ArgumentOutOfRangeException(nameof(n), n, "The occurrence index cannot be negative.");

        return frequency switch
        {
            ChecklistFrequency.Daily => anchor.AddDays(n),
            ChecklistFrequency.Weekly => anchor.AddDays(7 * n),
            ChecklistFrequency.Monthly => anchor.AddMonths(n), // clamps to the month's last day, from the ANCHOR each time
            ChecklistFrequency.Yearly => anchor.AddYears(n),   // 29-Feb -> 28-Feb in common years, from the ANCHOR each time
            _ => throw new ArgumentException($"Unknown frequency '{frequency}'.", nameof(frequency)),
        };
    }

    /// <summary>
    /// The first cycle date on or after <paramref name="date"/> (the anchor itself when the date is not after it). Used
    /// for a checklist's first occurrence and when an open occurrence is re-dated (frequency change, Mold -&gt; Machine).
    /// </summary>
    public static DateOnly FirstOnOrAfter(DateOnly anchor, string frequency, DateOnly date)
    {
        if (date <= anchor)
        {
            _ = OccurrenceAt(anchor, frequency, 0); // validates the frequency
            return anchor;
        }

        // Start from an index that can only be at or below the answer, then step forward. Occurrences strictly increase
        // with n for every frequency, so this ends after at most a couple of steps.
        var n = frequency switch
        {
            ChecklistFrequency.Daily => date.DayNumber - anchor.DayNumber,
            ChecklistFrequency.Weekly => (date.DayNumber - anchor.DayNumber) / 7,
            ChecklistFrequency.Monthly => Math.Max(0, (date.Year - anchor.Year) * 12 + (date.Month - anchor.Month) - 1),
            ChecklistFrequency.Yearly => Math.Max(0, date.Year - anchor.Year - 1),
            _ => throw new ArgumentException($"Unknown frequency '{frequency}'.", nameof(frequency)),
        };

        while (OccurrenceAt(anchor, frequency, n) < date)
        {
            n++;
        }

        return OccurrenceAt(anchor, frequency, n);
    }

    /// <summary>The first cycle date strictly after <paramref name="date"/>.</summary>
    public static DateOnly FirstAfter(DateOnly anchor, string frequency, DateOnly date) =>
        FirstOnOrAfter(anchor, frequency, date.AddDays(1));

    /// <summary>
    /// The ONE successor after an occurrence due on <paramref name="dueDate"/> is completed on <paramref name="completedOn"/>:
    /// the first cycle date strictly after both. Missed cycle dates in between are skipped, never generated.
    /// </summary>
    public static DateOnly NextDueAfterCompletion(DateOnly anchor, string frequency, DateOnly dueDate, DateOnly completedOn) =>
        FirstAfter(anchor, frequency, completedOn > dueDate ? completedOn : dueDate);
}
