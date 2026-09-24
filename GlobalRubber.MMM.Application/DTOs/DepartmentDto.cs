namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>Department information returned to API callers (masters.department_master).</summary>
public sealed class DepartmentDto
{
    public int DepartmentId { get; init; }
    public string DepartmentCode { get; init; } = string.Empty;
    public string DepartmentName { get; init; } = string.Empty;
    public string? Remarks { get; init; }
    public bool IsActive { get; init; }

    /// <summary>
    /// Base64 row_version. The client keeps it and sends it back on PUT: if the department changed in
    /// between, the update is refused with 409. Opaque - never interpreted client-side.
    /// </summary>
    public string RowVersion { get; init; } = string.Empty;
}
