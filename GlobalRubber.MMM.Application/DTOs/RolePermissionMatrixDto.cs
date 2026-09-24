namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// The complete permission matrix for one role - one <see cref="ModulePermissionDto"/> per
/// active module. Carries role context so the frontend can render "Edit Role: {RoleName}"
/// without a second call. A module with no permission_master row for this role appears here
/// with every action false ("missing row means no access" - see the seed script's own comment
/// in 010_seed_data.sql), it is not silently created.
/// </summary>
public sealed class RolePermissionMatrixDto
{
    public int RoleId { get; init; }
    public string RoleCode { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public IReadOnlyList<ModulePermissionDto> Permissions { get; init; } = Array.Empty<ModulePermissionDto>();
}
