namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for POST /api/v1/roles. Deliberately has no IsSystemRole property: whether a
/// role is a system role is never client-controlled - a role created through the API is always
/// a non-system role, and a client that still sends "isSystemRole" has it ignored by model
/// binding rather than honoured. RoleCode/RoleName/Description are normalized (trimmed, code
/// upper-cased) and validated by RoleService, not here.
/// </summary>
public sealed class CreateRoleRequest
{
    public string RoleCode { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public string? Description { get; init; }
    public bool IsActive { get; init; } = true;
}
