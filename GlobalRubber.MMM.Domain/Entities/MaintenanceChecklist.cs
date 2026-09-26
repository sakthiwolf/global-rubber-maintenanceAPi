using GlobalRubber.MMM.Domain.Common;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to masters.maintenance_checklist_master (the checklist header). Its items live in
/// masters.maintenance_checklist_item_master (FK_checklist_item_master_checklist, ON DELETE CASCADE). The header is
/// referenced by transactions.machine_pm_transaction / mold_pm_transaction (checklist_id). created_by/updated_by stay
/// plain ids like every mapped table.
///
/// Frequency and MachineId (migration 012) drive the recurring Machine PM: an ACTIVE checklist has a Frequency, and an
/// active Machine checklist has the Machine it belongs to (a Mold checklist never has one). Both stay nullable because
/// inactive legacy checklists may keep an incomplete configuration (CK_maintenance_checklist_master_active_config).
/// StartDate (migration 013) is the recurring cycle's permanent anchor and the due date of the first occurrence; an
/// active Machine checklist always has one (CK_maintenance_checklist_master_start_date). Inactive legacy checklists
/// without one fall back to CreatedAt as a plant (IST) date - the anchor used before migration 013.
/// </summary>
public class MaintenanceChecklist : AuditableEntity
{
    public int ChecklistId { get; set; }
    public string ChecklistCode { get; set; } = string.Empty;
    public string ChecklistName { get; set; } = string.Empty;
    public string AppliesTo { get; set; } = string.Empty;

    /// <summary>Daily / Weekly / Monthly / Yearly (ChecklistFrequency); null only on inactive legacy checklists.</summary>
    public string? Frequency { get; set; }

    /// <summary>The machine a Machine checklist belongs to; always null for Mold.</summary>
    public int? MachineId { get; set; }

    /// <summary>The recurring cycle's anchor and the first occurrence's due date (plant date).</summary>
    public DateOnly? StartDate { get; set; }

    public bool IsActive { get; set; } = true;

    public Machine? Machine { get; set; }
    public List<MaintenanceChecklistItem> Items { get; set; } = new();

    /// <summary>The date every occurrence is computed from: StartDate, or the creation date (IST) on legacy rows without one.</summary>
    public DateOnly CycleAnchor() => StartDate ?? PlantTime.ToPlantDate(CreatedAt);
}
