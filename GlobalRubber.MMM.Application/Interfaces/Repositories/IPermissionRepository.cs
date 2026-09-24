using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IPermissionRepository
{
    /// <summary>
    /// Single permission lookup by RoleCode+ModuleCode, for authorization checks. Projects
    /// only the six action columns (no Include, no full Role/Module/User graph) - null means
    /// no permission_master row exists for that (role, module) pair, i.e. "no access", not an
    /// error.
    /// </summary>
    Task<PermissionActionFlags?> GetPermissionFlagsAsync(
        string roleCode, string moduleCode, CancellationToken cancellationToken);

    /// <summary>
    /// Active modules, ordered for matrix display. Scoped narrowly to what the permission
    /// matrix needs - not a general module repository (that belongs to the Module/Menu step).
    /// </summary>
    Task<IReadOnlyList<Module>> GetActiveModulesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<Permission>> GetByRoleIdAsync(int roleId, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts the given permissions for the role: updates the row if (RoleId, ModuleId)
    /// already exists, otherwise inserts one. Everything happens through a single
    /// SaveChangesAsync call, which EF Core wraps in one implicit transaction, so a role's
    /// permission matrix is never partially saved. Modules not present in
    /// <paramref name="permissions"/> are left untouched.
    /// </summary>
    Task SaveRolePermissionsAsync(int roleId, IReadOnlyList<Permission> permissions, CancellationToken cancellationToken);
}
