using GlobalRubber.MMM.Domain.Common;

namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to security.module_master. This is the only menu-related table in the database -
/// there is no separate "menu" table. Each row is a page-level module that doubles as the
/// navigation item it appears as.
/// </summary>
public class Module : AuditableEntity
{
    public int ModuleId { get; set; }
    public string ModuleCode { get; set; } = string.Empty;
    public string ModuleName { get; set; } = string.Empty;
    public int? ParentModuleId { get; set; }
    public string MenuGroup { get; set; } = string.Empty;
    public string? Route { get; set; }
    public string? Icon { get; set; }
    public int SortOrder { get; set; }
    public bool IsMenuVisible { get; set; } = true;
    public bool IsActive { get; set; } = true;

    public Module? ParentModule { get; set; }
    public ICollection<Module> ChildModules { get; set; } = new List<Module>();
    public ICollection<Permission> Permissions { get; set; } = new List<Permission>();
}
