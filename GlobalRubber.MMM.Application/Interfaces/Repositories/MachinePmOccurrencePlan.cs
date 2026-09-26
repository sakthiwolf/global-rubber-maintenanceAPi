using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

/// <summary>
/// The business side of writing a recurring Machine PM occurrence, handed by a service to a repository that performs the
/// writes inside its own transaction (so the service decides WHAT is written, the repository only HOW and atomically).
/// </summary>
public sealed class MachinePmOccurrencePlan
{
    /// <summary>
    /// Builds the occurrence (header + checklist snapshot) from the checklist AS SAVED in the transaction - its id and its
    /// items' ids are known. The repository assigns the PM number from the MACHINE_PM sequence.
    /// </summary>
    public required Func<MaintenanceChecklist, MachinePm> BuildOccurrence { get; init; }

    /// <summary>
    /// Applies the machine-date rule to the LOCKED machine row, given the due dates of all the machine's open occurrences
    /// (Scheduled / In Progress) including the one just written.
    /// </summary>
    public required Action<Machine, IReadOnlyList<DateOnly>> ApplyToLockedMachine { get; init; }
}
