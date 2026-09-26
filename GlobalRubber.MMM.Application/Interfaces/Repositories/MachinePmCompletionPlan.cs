using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

/// <summary>
/// What completing a Machine PM does besides completing it (Function 5), decided by the service and applied by the
/// repository inside the completion's own transaction.
/// </summary>
public sealed class MachinePmCompletionPlan
{
    /// <summary>
    /// Machines to lock before anything changes (the PM's machine and the checklist's machine, which the successor goes on).
    /// The repository adds the PM's machine and locks all of them in ascending id order.
    /// </summary>
    public required IReadOnlyCollection<int> MachineIdsToLock { get; init; }

    /// <summary>
    /// Called inside the transaction AFTER the PM has been completed, with the checklist as it is NOW (its current items,
    /// or null when the PM has no checklist). Returns the successor to insert, or null when none is due (inactive or Mold
    /// checklist, incomplete configuration). The repository issues its MACHINE_PM number.
    /// </summary>
    public required Func<MaintenanceChecklist?, MachinePm?> BuildSuccessor { get; init; }

    /// <summary>The machine-date rule, applied to every locked machine with the due dates of its open occurrences.</summary>
    public required Action<Machine, IReadOnlyList<DateOnly>> ApplyToLockedMachine { get; init; }
}
