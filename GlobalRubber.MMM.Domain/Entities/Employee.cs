using GlobalRubber.MMM.Domain.Common;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to masters.employee_master. CreatedBy/UpdatedBy stay plain nullable ids (see AuditableEntity), the same as
/// every other mapped table. Referenced by machines, molds, users and several transactions - none of those are
/// mapped yet, so no navigation collections are exposed.
/// </summary>
public class Employee : AuditableEntity
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public int DepartmentId { get; set; }
    public string? Mobile { get; set; }
    public string? Email { get; set; }
    public bool IsActive { get; set; } = true;

    public Department Department { get; set; } = null!;
}
