using GlobalRubber.MMM.Domain.Common;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to masters.department_master. Referenced (NO ACTION foreign keys) by masters.machine_master,
/// masters.employee_master and security.user_master - those are out of scope here, so no navigation
/// collections are exposed until those modules exist.
/// </summary>
public class Department : AuditableEntity
{
    public int DepartmentId { get; set; }
    public string DepartmentCode { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string? Remarks { get; set; }
    public bool IsActive { get; set; } = true;
}
