using GlobalRubber.MMM.Domain.Common;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>Maps to security.role_master.</summary>
public class Role : AuditableEntity
{
    public int RoleId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsSystemRole { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
    public ICollection<User> Users { get; set; } = new List<User>();
}
