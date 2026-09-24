namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for POST /api/v1/employees. Not accepted (and ignored by model binding if sent): employeeId,
/// employeeCode (issued from the EMPLOYEE document sequence), isActive (a new employee is always active), rowVersion
/// and the audit columns. Normalization and validation are EmployeeService's job.
/// </summary>
public sealed class CreateEmployeeRequest
{
    public string EmployeeName { get; init; } = string.Empty;
    public string Designation { get; init; } = string.Empty;
    public int DepartmentId { get; init; }
    public string? Mobile { get; init; }
    public string? Email { get; init; }
}
