using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IMaintenanceChecklistRepository
{
    /// <summary>A page of checklists (with their items, in sort order), name-ordered.</summary>
    Task<(IReadOnlyList<MaintenanceChecklist> Items, int TotalCount)> GetAllAsync(
        MaintenanceChecklistListQuery request, CancellationToken cancellationToken);

    /// <summary>One checklist with its items in sort order, untracked; null if it does not exist.</summary>
    Task<MaintenanceChecklist?> GetByIdAsync(int checklistId, CancellationToken cancellationToken);

    Task<bool> ExistsActiveByNameAsync(string name, int? excludeChecklistId, CancellationToken cancellationToken);

    /// <summary>The ACTIVE machines a Machine checklist can be assigned to, by code.</summary>
    Task<MaintenanceChecklistLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the header (code from the MAINTENANCE_CHECKLIST sequence) and its items and - when
    /// <paramref name="firstOccurrence"/> is given (an active Machine checklist) - its first Machine PM occurrence, all in
    /// ONE transaction: the machine row is locked, the occurrence built by the plan from the SAVED checklist (ids known) is
    /// inserted with a number from the MACHINE_PM sequence, and the plan sets the machine's next maintenance date from the
    /// machine's open occurrences. Any failure rolls back everything: checklist, items, occurrence, snapshot, machine
    /// change and both sequence numbers.
    /// </summary>
    Task<MaintenanceChecklist> AddAsync(MaintenanceChecklist checklist, MachinePmOccurrencePlan? firstOccurrence, CancellationToken cancellationToken);

    /// <summary>
    /// Writes name / applies-to / frequency / machine / updated columns guarded by <paramref name="originalRowVersion"/> and, when
    /// <paramref name="replaceItems"/> is true, replaces the item rows with <c>checklist.Items</c> - all in one transaction.
    /// When <paramref name="occurrenceSync"/> is given, the same transaction also applies it: the affected machine rows are
    /// locked first (ascending id), then after the header/items the plan decides what happens to the checklist's open
    /// occurrence (re-date, move, snapshot refresh, or a new first occurrence) and every locked machine's next date is
    /// recalculated. A stale row version or any failure rolls back everything.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The row version no longer matches.</exception>
    Task<MaintenanceChecklist> UpdateAsync(
        MaintenanceChecklist checklist, byte[] originalRowVersion, bool replaceItems, ChecklistOccurrenceSyncPlan? occurrenceSync,
        CancellationToken cancellationToken);

    /// <summary>Soft-deactivates (is_active = 0) guarded by the loaded row version. Items are untouched.</summary>
    Task<MaintenanceChecklist> DeactivateAsync(MaintenanceChecklist checklist, CancellationToken cancellationToken);
}
