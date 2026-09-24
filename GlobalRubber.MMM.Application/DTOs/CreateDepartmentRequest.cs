namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for POST /api/v1/departments. Only the two fields a user enters. Not accepted (and
/// ignored by model binding if sent): departmentId, departmentCode (issued from the DEPARTMENT document
/// sequence), isActive (a new department is always active), rowVersion and the audit columns.
/// Normalization (trim, blank remarks to null) and validation are DepartmentService's job.
/// </summary>
public sealed class CreateDepartmentRequest
{
    public string DepartmentName { get; init; } = string.Empty;
    public string? Remarks { get; init; }
}
