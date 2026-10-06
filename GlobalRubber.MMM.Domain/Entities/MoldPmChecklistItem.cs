namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to transactions.mold_pm_checklist_transaction: one checklist line of a mold PM - used by MANUAL mold PMs
/// (migration 025). ItemLabel is a SNAPSHOT copied when the PM is scheduled; ChecklistItemId points back at the Checklist
/// Master item while it exists (FK ON DELETE SET NULL). No audit columns or row_version - the PM header's guards it.
/// </summary>
public class MoldPmChecklistItem
{
    public int MoldPmChecklistId { get; set; }
    public int MoldPmId { get; set; }
    public int SortOrder { get; set; }
    public string ItemLabel { get; set; } = string.Empty;
    public int? ChecklistItemId { get; set; }
    public bool IsChecked { get; set; }
}
