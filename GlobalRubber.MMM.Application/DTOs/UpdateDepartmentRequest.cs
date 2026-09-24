namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for PUT /api/v1/departments/{id} - a full replacement of the editable fields plus the row
/// version the caller last read (stale-form protection: if the department changed since, the save is refused
/// with 409). Not accepted: departmentId, departmentCode (immutable), isActive (deactivation is DELETE),
/// audit columns.
/// </summary>
public sealed class UpdateDepartmentRequest
{
    public string DepartmentName { get; init; } = string.Empty;
    public string? Remarks { get; init; }

    /// <summary>Base64 row_version exactly as returned by DepartmentDto.RowVersion.</summary>
    public string RowVersion { get; init; } = string.Empty;
}
