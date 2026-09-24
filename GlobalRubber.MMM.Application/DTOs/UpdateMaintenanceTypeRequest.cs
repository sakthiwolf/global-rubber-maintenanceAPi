namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for PUT /api/v1/maintenance-types/{id} - a full replacement of the editable fields plus the row version
/// the caller last read (409 if it changed since). Not accepted: the id, the code (immutable), isActive (deactivation is
/// DELETE), audit columns.
/// </summary>
public sealed class UpdateMaintenanceTypeRequest : CreateMaintenanceTypeRequest
{
    /// <summary>Base64 row_version exactly as returned by MaintenanceTypeDto.RowVersion.</summary>
    public string RowVersion { get; init; } = string.Empty;
}
