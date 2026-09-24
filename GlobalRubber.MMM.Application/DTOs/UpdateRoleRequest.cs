namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Request body for PUT /api/v1/roles/{id} - a full replacement of the three editable fields.
/// Deliberately has no IsSystemRole and no IsActive: whether a role is a system role is never
/// client-controlled (a client that still sends "isSystemRole" has it ignored by model binding),
/// and activation/deactivation is a separate step. RoleCode is required even though a system
/// role's code cannot change - the client sends the current code back, and RoleService rejects
/// any different one. Normalization (trim, upper-case code, blank description to null) and
/// validation are RoleService's job, not this DTO's.
/// </summary>
public sealed class UpdateRoleRequest
{
    public string RoleCode { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public string? Description { get; init; }
}
