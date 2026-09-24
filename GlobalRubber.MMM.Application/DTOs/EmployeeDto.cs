namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>Employee information returned to API callers (masters.employee_master joined to its department).</summary>
public sealed class EmployeeDto
{
    public int EmployeeId { get; init; }
    public string EmployeeCode { get; init; } = string.Empty;
    public string EmployeeName { get; init; } = string.Empty;
    public string Designation { get; init; } = string.Empty;
    public int DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;

    /// <summary>Whether the employee's department is still active - lets a form keep an inactive one visible, flagged.</summary>
    public bool DepartmentIsActive { get; init; }

    public string? Mobile { get; init; }
    public string? Email { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAt { get; init; }
    public int? CreatedBy { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public int? UpdatedBy { get; init; }

    /// <summary>
    /// Base64 row_version. The client keeps it and sends it back on PUT: if the employee changed in between, the
    /// update is refused with 409. Opaque - never interpreted client-side.
    /// </summary>
    public string RowVersion { get; init; } = string.Empty;
}
