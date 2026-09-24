using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IRoleService
{
    Task<PagedResult<RoleDto>> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No role exists with the given id.</exception>
    Task<RoleDto> GetByIdAsync(int roleId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a non-system role. <paramref name="actingUserId"/> (the authenticated user's id
    /// from the JWT <c>sub</c> claim) and <paramref name="ipAddress"/> are supplied by the
    /// controller, following the same controller-to-service pattern as AuthService's ipAddress;
    /// they feed CreatedBy and the audit entry only.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">RoleCode/RoleName/Description fail validation.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The RoleCode or RoleName already exists.</exception>
    Task<RoleDto> CreateAsync(
        CreateRoleRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a role's RoleCode, RoleName and Description. <paramref name="actingUserId"/> /
    /// <paramref name="ipAddress"/> come from the controller exactly as for <see cref="CreateAsync"/>
    /// and feed UpdatedBy and the RoleUpdated audit entry only.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No role exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">RoleCode/RoleName/Description fail validation.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">
    /// The new RoleCode or RoleName already belongs to another role, a system role's code was
    /// changed, or the role was modified by someone else after it was loaded.
    /// </exception>
    Task<RoleDto> UpdateAsync(
        int roleId, UpdateRoleRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Soft-deactivates a role (IsActive = false). The row and its permission rows are kept.
    /// <paramref name="actingUserId"/> / <paramref name="ipAddress"/> come from the controller as
    /// for the other write operations and feed UpdatedBy and the RoleDeactivated audit entry only.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No role exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">
    /// The role is a system role, is already inactive, still has active users assigned, or was
    /// modified by someone else after it was loaded.
    /// </exception>
    Task<RoleDto> DeactivateAsync(
        int roleId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
