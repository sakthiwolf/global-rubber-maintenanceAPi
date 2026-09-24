using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IRoleRepository
{
    Task<(IReadOnlyList<Role> Items, int TotalCount)> GetAllAsync(
        PaginationRequest request, CancellationToken cancellationToken);

    Task<Role?> GetByIdAsync(int roleId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether any role (active or not) already uses this RoleCode. Expects the already-normalized
    /// code. <paramref name="excludeRoleId"/> leaves one role out of the check (the role being
    /// updated must not count as its own duplicate); null checks every role.
    /// </summary>
    Task<bool> ExistsByCodeAsync(string roleCode, int? excludeRoleId, CancellationToken cancellationToken);

    /// <summary>Case-insensitive RoleName match across all roles (active or not); see <c>ExistsByCodeAsync</c> for <paramref name="excludeRoleId"/>.</summary>
    Task<bool> ExistsByNameAsync(string roleName, int? excludeRoleId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the role and returns it with its generated RoleId. The RoleCode unique index is
    /// the final authority on code uniqueness: a violation of it (e.g. two concurrent creates
    /// that both passed <see cref="ExistsByCodeAsync"/>) surfaces as a ConflictException, never
    /// as a raw database exception.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The RoleCode already exists.</exception>
    Task<Role> AddAsync(Role role, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the edited RoleCode, RoleName, Description, UpdatedAt and UpdatedBy of a role that was
    /// loaded through <see cref="GetByIdAsync"/> (and so carries the row_version it was read with).
    /// Nothing else is written - IsSystemRole/IsActive/CreatedAt/CreatedBy can never be changed
    /// through this method. The existing row_version concurrency token is the guard: if the row
    /// changed (or was removed) after it was loaded, the save is refused.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">
    /// The role was modified by someone else after it was loaded, or the new RoleCode already exists.
    /// </exception>
    Task<Role> UpdateAsync(Role role, CancellationToken cancellationToken);

    /// <summary>
    /// Soft deactivation: persists ONLY IsActive, UpdatedAt and UpdatedBy of a role that was loaded
    /// through <see cref="GetByIdAsync"/> (so it carries the row_version it was read with). The row
    /// is never deleted, and no other column - and no permission row - is touched. Same
    /// optimistic-concurrency guard as <see cref="UpdateAsync"/>.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">
    /// The role was modified by someone else (or removed) after it was loaded.
    /// </exception>
    Task<Role> DeactivateAsync(Role role, CancellationToken cancellationToken);
}
