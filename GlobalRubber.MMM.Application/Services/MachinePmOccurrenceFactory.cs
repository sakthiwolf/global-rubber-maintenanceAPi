using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Builds recurring Machine PM occurrences - one place for every path that creates one: a new Machine checklist
/// (Function 3), Mold -&gt; Machine (Function 4) and the successor after a completion (Function 5).
/// </summary>
internal static class MachinePmOccurrenceFactory
{
    /// <summary>
    /// A Scheduled occurrence of <paramref name="checklist"/> due on <paramref name="dueDate"/>, on the checklist's machine,
    /// with a snapshot of the checklist's CURRENT items. No maintenance type, engineer or maintenance-by (approved Q4/Q5).
    /// The PM number is issued by the repository from the MACHINE_PM sequence.
    /// </summary>
    public static MachinePm NewOccurrence(MaintenanceChecklist checklist, DateOnly dueDate, DateTime now, int? actingUserId) => new()
    {
        MachineId = checklist.MachineId!.Value,
        ChecklistId = checklist.ChecklistId,
        ScheduledDate = dueDate,
        Status = MachinePmStatus.Scheduled,
        MaintenanceTypeId = null,
        EngineerId = null,
        MaintenanceBy = null,
        CreatedAt = now,
        CreatedBy = actingUserId,
        ChecklistItems = Snapshot(checklist),
    };

    /// <summary>
    /// An occurrence's checklist snapshot: label, order and source item id copied now, unchecked. Only an OPEN occurrence's
    /// snapshot is ever replaced; a completed one is history.
    /// </summary>
    public static List<MachinePmChecklistItem> Snapshot(MaintenanceChecklist checklist) =>
        checklist.Items
            .OrderBy(i => i.SortOrder).ThenBy(i => i.ChecklistItemId)
            .Select(i => new MachinePmChecklistItem { SortOrder = i.SortOrder, ItemLabel = i.ItemLabel, ChecklistItemId = i.ChecklistItemId, IsChecked = false })
            .ToList();
}
