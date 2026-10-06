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
///
/// Migration 020: the table holds two kinds of row. A CHECKLIST MASTER is a reusable checklist (name, applies-to, items)
/// with NO plan configuration - Frequency, MachineId, StartDate, MaintenanceTypeId and SourceChecklistId are all null. A
/// PREVENTIVE MAINTENANCE PLAN always has a Frequency; a Machine plan uses a checklist master through SourceChecklistId
/// and then has no items of its own - its PM occurrences copy the master's items. Plans created before migration 020
/// (and every Mold plan) have no source and keep their own items (CK_maintenance_checklist_master_active_config / _source).
/// </summary>
public class MaintenanceChecklist : AuditableEntity
{
    public int ChecklistId { get; set; }
    public string ChecklistCode { get; set; } = string.Empty;
    public string ChecklistName { get; set; } = string.Empty;
    public string AppliesTo { get; set; } = string.Empty;

    /// <summary>Daily / Weekly / Monthly / Yearly (ChecklistFrequency); always set on a plan, null on a checklist master (migration 020).</summary>
    public string? Frequency { get; set; }

    /// <summary>The machine a Machine checklist belongs to; always null for Mold.</summary>
    public int? MachineId { get; set; }

    /// <summary>The recurring cycle's anchor and the first occurrence's due date (plant date).</summary>
    public DateOnly? StartDate { get; set; }

    /// <summary>
    /// Migration 017: the optional maintenance type every PM occurrence of a Machine checklist carries (copied on creation,
    /// kept in step on the open occurrences). Always null for Mold - mold PMs have no maintenance type.
    /// </summary>
    public int? MaintenanceTypeId { get; set; }

    /// <summary>
    /// Migration 020: the checklist master a Machine plan uses (FK_maintenance_checklist_master_source). Null on checklist
    /// masters, Mold plans and plans created before migration 020 (which keep their own items).
    /// </summary>
    public int? SourceChecklistId { get; set; }

    public bool IsActive { get; set; } = true;

    public Machine? Machine { get; set; }
    public MaintenanceChecklist? SourceChecklist { get; set; }
    public MaintenanceType? MaintenanceType { get; set; }
    public List<MaintenanceChecklistItem> Items { get; set; } = new();

    /// <summary>The date every occurrence is computed from: StartDate, or the creation date (IST) on legacy rows without one.</summary>
    public DateOnly CycleAnchor() => StartDate ?? PlantTime.ToPlantDate(CreatedAt);

    /// <summary>A reusable checklist master (no frequency) rather than a preventive maintenance plan.</summary>
    public bool IsChecklistMaster => Frequency is null;

    /// <summary>
    /// The items the plan's PM occurrences use: the checklist master's items when the plan has a source, otherwise its own.
    /// The source must have been loaded with its items.
    /// </summary>
    public IReadOnlyList<MaintenanceChecklistItem> EffectiveItems() =>
        SourceChecklistId is null
            ? Items
            : SourceChecklist?.Items ?? throw new InvalidOperationException($"The checklist master of checklist {ChecklistId} was not loaded.");
}
