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
    Task<RolePermissionMatrixDto> UpdateRolePermissionsAsync(
        int roleId, UpdateRolePermissionsRequest request, CancellationToken cancellationToken);
}
