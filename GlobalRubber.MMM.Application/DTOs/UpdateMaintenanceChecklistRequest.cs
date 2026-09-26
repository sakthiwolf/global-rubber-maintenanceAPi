namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for PUT /api/v1/maintenance-checklists/{id} - a full replacement of the editable fields AND the item list
/// (items are replaced as a set - analysis 13.x), plus the row version the caller last read (409 if it changed since).
/// Not accepted: the id, the code (immutable), isActive (deactivation is DELETE), audit columns.
/// </summary>
public sealed class UpdateMaintenanceChecklistRequest : CreateMaintenanceChecklistRequest
{
    /// <summary>Base64 row_version exactly as returned by MaintenanceChecklistDto.RowVersion.</summary>
    public string RowVersion { get; init; } = string.Empty;
}
