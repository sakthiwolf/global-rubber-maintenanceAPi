using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

/// <summary>
/// What a checklist UPDATE does to the checklist's open Machine PM occurrence (Function 4), decided by the service and
/// applied by the repository inside the update's own transaction.
/// </summary>
public sealed class ChecklistOccurrenceSyncPlan
{
    /// <summary>
    /// Machines whose rows must be locked before anything changes (the checklist's current and new machine). The
    /// repository adds the machine the open occurrence is on, then locks all of them in ascending id order.
    /// </summary>
    public required IReadOnlyCollection<int> MachineIdsToLock { get; init; }

    /// <summary>
    /// Called inside the transaction with the checklist AS SAVED (new item ids known) and its open occurrence (tracked,
    /// snapshot lines loaded; null when there is none). May change the occurrence's machine / due date in place, and
    /// returns the snapshot to put in place of the open occurrence's lines and/or a brand-new occurrence to insert.
    /// </summary>
    public required Func<MaintenanceChecklist, MachinePm?, OpenOccurrenceChange> Decide { get; init; }

    /// <summary>The machine-date rule, applied to every locked machine with the due dates of its open occurrences.</summary>
    public required Action<Machine, IReadOnlyList<DateOnly>> ApplyToLockedMachine { get; init; }
}

/// <summary>The result of <see cref="ChecklistOccurrenceSyncPlan.Decide"/>.</summary>
public sealed class OpenOccurrenceChange
{
    public static readonly OpenOccurrenceChange None = new();

    /// <summary>Replaces the OPEN occurrence's snapshot lines (never a completed occurrence's).</summary>
    public IReadOnlyList<MachinePmChecklistItem>? ReplacementSnapshot { get; init; }

    /// <summary>A first occurrence to insert (Mold -&gt; Machine with no open occurrence); gets a MACHINE_PM number.</summary>
    public MachinePm? NewOccurrence { get; init; }
}
