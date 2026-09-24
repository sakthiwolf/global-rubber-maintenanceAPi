namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for PUT /api/v1/employees/{id} - a full replacement of the editable fields plus the row version the
/// caller last read (409 if the employee changed since). Not accepted: employeeId, employeeCode (immutable),
/// isActive (deactivation is DELETE), audit columns.
/// </summary>
public sealed class UpdateEmployeeRequest
{
    public string EmployeeName { get; init; } = string.Empty;
    public string Designation { get; init; } = string.Empty;
    public int DepartmentId { get; init; }
    public string? Mobile { get; init; }
    public string? Email { get; init; }

    /// <summary>Base64 row_version exactly as returned by EmployeeDto.RowVersion.</summary>
    public string RowVersion { get; init; } = string.Empty;
}
