namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to transactions.machine_pm_checklist_transaction: one checklist line of a machine PM. ItemLabel is a SNAPSHOT
/// copied when the PM is scheduled; ChecklistItemId points back at the master item while it exists (FK ON DELETE SET
/// NULL). The table has no audit columns and no row_version - the PM header's row_version guards it.
/// </summary>
public class MachinePmChecklistItem
{
    public int MachinePmChecklistId { get; set; }
    public int MachinePmId { get; set; }
    public int SortOrder { get; set; }
    public string ItemLabel { get; set; } = string.Empty;
    public int? ChecklistItemId { get; set; }
    public bool IsChecked { get; set; }
}
