namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for POST /api/v1/maintenance-checklists/masters - a reusable Checklist Master (migration 020): Name*,
/// Applies To* and at least one non-blank item (analysis 4.10 / BR-34 - the checklist rules). It has no frequency, machine,
/// start date or maintenance type: those belong to the preventive maintenance plans that use it. Not accepted: id, code
/// (MAINTENANCE_CHECKLIST sequence), isActive (always active when created), item ids, audit columns.
/// </summary>
public class CreateChecklistMasterRequest
{
    public string ChecklistName { get; init; } = string.Empty;

    /// <summary>Required: Machine / Mold (CK_maintenance_checklist_master_applies_to).</summary>
    public string? AppliesTo { get; init; }

    /// <summary>The items in display order; blank rows are dropped and sort_order is assigned from the position (1, 2, 3...).</summary>
    public IReadOnlyList<MaintenanceChecklistItemRequest>? Items { get; init; }
}

/// <summary>Request body for PUT /api/v1/maintenance-checklists/masters/{id}: name, applies-to, the item list (replaced as a set) and the row version.</summary>
public sealed class UpdateChecklistMasterRequest : CreateChecklistMasterRequest
{
    /// <summary>Base64 row_version exactly as returned by MaintenanceChecklistDto.RowVersion.</summary>
    public string RowVersion { get; init; } = string.Empty;
}

/// <summary>
/// One ACTIVE Checklist Master offered in the plan form's "Checklist Master" dropdown
/// (GET /api/v1/maintenance-checklists/masters/options), with its items so selecting it shows them without another call.
/// </summary>
public sealed class ChecklistMasterOptionDto
{
    public int ChecklistId { get; init; }
    public string ChecklistCode { get; init; } = string.Empty;
    public string ChecklistName { get; init; } = string.Empty;
    public string AppliesTo { get; init; } = string.Empty;
    public IReadOnlyList<MaintenanceChecklistItemDto> Items { get; init; } = Array.Empty<MaintenanceChecklistItemDto>();
}
