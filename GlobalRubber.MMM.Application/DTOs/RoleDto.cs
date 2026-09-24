namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Role information returned to API callers. Deliberately flat - no Permissions or Users
/// collection. A permission/user count was considered and left out: every role already has one
/// permission_master row per module regardless of what's granted, so a raw row count would be
/// the same (module count) for every role and would not mean "permissions granted"; a user
/// count would need its own aggregate query for a field nothing in this step asked for.
/// </summary>
public sealed class RoleDto
{
    public int RoleId { get; init; }
    public string RoleCode { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsSystemRole { get; init; }
    public bool IsActive { get; init; }
}
