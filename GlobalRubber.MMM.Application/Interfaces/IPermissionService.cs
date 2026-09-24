using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IPermissionService
{
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No role exists with the given id.</exception>
    Task<RolePermissionMatrixDto> GetRolePermissionsAsync(int roleId, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No role exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">
    /// The request contains a duplicate moduleId or a moduleId that is not an active module.
    /// </exception>
    /// <remarks>
    /// <paramref name="actingUserId"/> (the JWT <c>sub</c> claim) and <paramref name="ipAddress"/> come from
    /// the controller, as for RoleService's writes, and feed CreatedBy/UpdatedBy and the
    /// PermissionsUpdated audit entry (with one audit_log_detail row per changed flag) only.
    /// </remarks>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">
    /// The permissions were modified by someone else while this request was saving.
    /// </exception>
    Task<RolePermissionMatrixDto> UpdateRolePermissionsAsync(
        int roleId, UpdateRolePermissionsRequest request, int? actingUserId, string? ipAddress,
        CancellationToken cancellationToken);

    /// <summary>
    /// The signed-in user's OWN permission matrix, resolved from their user record's role - never
    /// from a caller-supplied role id. Any authenticated user may read it (the frontend needs it
    /// to build the menu and gate actions), unlike <see cref="GetRolePermissionsAsync"/>, which is
    /// an administrative read of any role and requires AdminRoles.View.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">The token's user no longer exists.</exception>
    Task<RolePermissionMatrixDto> GetMyPermissionsAsync(int userId, CancellationToken cancellationToken);
}
