namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for POST /api/v1/maintenance-checklists (analysis 4.10: Name*, Applies To*, at least one non-blank item).
/// Not accepted (and ignored by model binding if sent): checklistId, checklistCode (issued from the MAINTENANCE_CHECKLIST
/// document sequence), isActive (a new checklist is always active), item ids, rowVersion and the audit columns.
/// </summary>
public class CreateMaintenanceChecklistRequest
{
    public string ChecklistName { get; init; } = string.Empty;

    /// <summary>Required: Machine / Mold (CK_maintenance_checklist_master_applies_to).</summary>
    public string? AppliesTo { get; init; }

    /// <summary>
    /// Daily / Weekly / Monthly / Yearly (CK_maintenance_checklist_master_frequency). Required for an active checklist -
    /// a new checklist is always active.
    /// </summary>
    public string? Frequency { get; init; }

    /// <summary>
    /// Machine checklists: the machine it belongs to - required while the checklist is active, and a newly chosen machine
    /// must be active. Mold checklists: must be empty (CK_maintenance_checklist_master_machine).
    /// </summary>
    public int? MachineId { get; init; }

    /// <summary>
    /// yyyy-MM-dd. The recurring cycle's permanent anchor; for a Machine checklist it is also the due date of the first
    /// occurrence. Required for an active Machine checklist (CK_maintenance_checklist_master_start_date); optional for Mold.
    /// </summary>
    public DateOnly? StartDate { get; init; }

    /// <summary>
    /// Optional (migration 017). Machine checklists: the maintenance type every PM occurrence of the checklist carries - it
    /// must apply to Machine or Both, and a newly chosen type must be active. Mold checklists: must be empty (mold PMs have
    /// no maintenance type).
    /// </summary>
    public int? MaintenanceTypeId { get; init; }

    /// <summary>
    /// Migration 020. Machine plans: the ACTIVE Checklist Master (applies to Machine, at least one item) whose items the
    /// plan's PM occurrences use - required, except when editing a plan created before migration 020 that has none yet
    /// (it keeps its own items until one is chosen). Mold plans: must be empty.
    /// </summary>
    public int? SourceChecklistId { get; init; }

    /// <summary>
    /// The items in the order they should appear; blank rows are dropped (analysis 4.10) and sort_order is assigned from
    /// the position of the remaining ones (1, 2, 3...), so the order cannot be inconsistent. Must be empty when a
    /// Checklist Master is selected - the plan then has no items of its own (migration 020).
    /// </summary>
    public IReadOnlyList<MaintenanceChecklistItemRequest>? Items { get; init; }
}

/// <summary>One checklist item row as entered.</summary>
public sealed class MaintenanceChecklistItemRequest
{
    public string? ItemLabel { get; init; }
}
