using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

public sealed class PermissionRepository : IPermissionRepository
{
    private readonly GlobalRubberDbContext _dbContext;

    public PermissionRepository(GlobalRubberDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<PermissionActionFlags?> GetPermissionFlagsAsync(
        string roleCode, string moduleCode, CancellationToken cancellationToken) =>
        await _dbContext.Permissions
            .AsNoTracking()
            .Where(p => p.Role.RoleCode == roleCode && p.Module.ModuleCode == moduleCode)
            .Select(p => new PermissionActionFlags(p.CanView, p.CanAdd, p.CanEdit, p.CanDelete, p.CanApprove, p.CanExport))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<Module>> GetActiveModulesAsync(CancellationToken cancellationToken) =>
        await _dbContext.Modules
            .AsNoTracking()
            .Where(m => m.IsActive)
            .OrderBy(m => m.SortOrder)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Permission>> GetByRoleIdAsync(int roleId, CancellationToken cancellationToken) =>
        await _dbContext.Permissions
            .AsNoTracking()
            .Where(p => p.RoleId == roleId)
            .ToListAsync(cancellationToken);

    public async Task SaveRolePermissionsAsync(
        int roleId, IReadOnlyList<Permission> permissions, CancellationToken cancellationToken)
    {
        var moduleIds = permissions.Select(p => p.ModuleId).ToList();

        // Tracked (no AsNoTracking): SaveChangesAsync needs to see these as the original values
        // for the row_version concurrency check EF already performs automatically.
        var existingByModuleId = await _dbContext.Permissions
            .Where(p => p.RoleId == roleId && moduleIds.Contains(p.ModuleId))
            .ToDictionaryAsync(p => p.ModuleId, cancellationToken);

        foreach (var incoming in permissions)
        {
            if (existingByModuleId.TryGetValue(incoming.ModuleId, out var existing))
            {
                existing.CanView = incoming.CanView;
                existing.CanAdd = incoming.CanAdd;
                existing.CanEdit = incoming.CanEdit;
                existing.CanDelete = incoming.CanDelete;
                existing.CanApprove = incoming.CanApprove;
                existing.CanExport = incoming.CanExport;
                existing.UpdatedAt = incoming.UpdatedAt;
                existing.UpdatedBy = incoming.UpdatedBy;
            }
            else
            {
                incoming.RoleId = roleId;
                _dbContext.Permissions.Add(incoming);
            }
        }

        // Single call: every insert/update for this role's whole matrix goes through one
        // implicit transaction, so a partial save is not possible.
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
