namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to masters.maintenance_checklist_item_master (child of maintenance_checklist_master). The table has the
/// created/updated columns but NO row_version, so it does not derive from AuditableEntity - the header's row_version
/// guards the whole checklist. PM checklist rows copy item_label as a snapshot and point back here through
/// checklist_item_id (ON DELETE SET NULL), so replacing the items never changes PM history.
/// </summary>
public class MaintenanceChecklistItem
{
    public int ChecklistItemId { get; set; }
    public int ChecklistId { get; set; }
    public int SortOrder { get; set; }
    public string ItemLabel { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public int? UpdatedBy { get; set; }
}
