using GlobalRubber.MMM.Domain.Common;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>Maps to security.permission_master - one row per (role, module) combination.</summary>
public class Permission : AuditableEntity
{
    public int PermissionId { get; set; }
    public int RoleId { get; set; }
    public int ModuleId { get; set; }
    public bool CanView { get; set; }
    public bool CanAdd { get; set; }
    public bool CanEdit { get; set; }
    public bool CanDelete { get; set; }
    public bool CanApprove { get; set; }
    public bool CanExport { get; set; }

    public Role Role { get; set; } = null!;
    public Module Module { get; set; } = null!;
}
