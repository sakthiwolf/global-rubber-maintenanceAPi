namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for PUT /api/v1/roles/{roleId}/permissions. "Select All"/"Clear All" are
/// frontend-only conveniences (not persisted) - the API only ever receives the resulting
/// individual permission values, one entry per module being changed. Modules omitted from
/// this list are left untouched, not cleared.
/// </summary>
public sealed class UpdateRolePermissionsRequest
{
    public IReadOnlyList<ModulePermissionUpdateDto> Permissions { get; init; } = Array.Empty<ModulePermissionUpdateDto>();
}

public sealed class ModulePermissionUpdateDto
{
    public int ModuleId { get; init; }
    public bool CanView { get; init; }
    public bool CanAdd { get; init; }
    public bool CanEdit { get; init; }
    public bool CanDelete { get; init; }
    public bool CanApprove { get; init; }
    public bool CanExport { get; init; }
}
