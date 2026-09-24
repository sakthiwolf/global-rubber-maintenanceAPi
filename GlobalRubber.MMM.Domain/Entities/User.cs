using GlobalRubber.MMM.Domain.Common;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to security.user_master. EmployeeId/DepartmentId reference masters.employee_master
/// and masters.department_master, which are out of scope for this phase - kept as plain
/// nullable ids (no navigation property) until those entities exist.
/// </summary>
public class User : AuditableEntity
{
    public int UserId { get; set; }
    public string UserCode { get; set; } = string.Empty;
    public string LoginId { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public int? EmployeeId { get; set; }
    public int? DepartmentId { get; set; }
    public string? Email { get; set; }
    public string? Mobile { get; set; }
    public bool MustChangePassword { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool IsActive { get; set; } = true;

    public Role Role { get; set; } = null!;
}
