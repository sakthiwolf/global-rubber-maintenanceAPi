namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// One row of a role's permission matrix - a module plus the actions granted to a role for it.
/// Property names match security.permission_master columns exactly (CanAdd, not CanCreate -
/// the database column is can_add, not can_create).
/// </summary>
public sealed class ModulePermissionDto
{
    public int ModuleId { get; init; }
    public string ModuleCode { get; init; } = string.Empty;
    public string ModuleName { get; init; } = string.Empty;
    public string MenuGroup { get; init; } = string.Empty;
    public int SortOrder { get; init; }
    public bool CanView { get; init; }
    public bool CanAdd { get; init; }
    public bool CanEdit { get; init; }
    public bool CanDelete { get; init; }
    public bool CanApprove { get; init; }
    public bool CanExport { get; init; }
}
