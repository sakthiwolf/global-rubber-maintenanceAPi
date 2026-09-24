using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

/// <summary>
/// No <c>.Include(r => r.Users)</c>/<c>.Include(r => r.Permissions)</c> here - normal role
/// listing/detail only needs the scalar Role columns that RoleDto maps from.
/// </summary>
public sealed class RoleRepository : IRoleRepository
{
    private readonly GlobalRubberDbContext _dbContext;

    public RoleRepository(GlobalRubberDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(IReadOnlyList<Role> Items, int TotalCount)> GetAllAsync(
        PaginationRequest request, CancellationToken cancellationToken)
    {
        var query = _dbContext.Roles
            .AsNoTracking()
            .OrderBy(r => r.RoleName);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<Role?> GetByIdAsync(int roleId, CancellationToken cancellationToken) =>
        _dbContext.Roles
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.RoleId == roleId, cancellationToken);

    public Task<bool> ExistsByCodeAsync(string roleCode, int? excludeRoleId, CancellationToken cancellationToken) =>
        _dbContext.Roles.AsNoTracking()
            .AnyAsync(r => r.RoleCode == roleCode && r.RoleId != excludeRoleId, cancellationToken);

    public Task<bool> ExistsByNameAsync(string roleName, int? excludeRoleId, CancellationToken cancellationToken)
    {
        // Explicit ToLower rather than relying on the column collation being case-insensitive.
        var lowered = roleName.ToLower();

        return _dbContext.Roles.AsNoTracking()
            .AnyAsync(r => r.RoleName.ToLower() == lowered && r.RoleId != excludeRoleId, cancellationToken);
    }

    public async Task<Role> AddAsync(Role role, CancellationToken cancellationToken)
    {
        _dbContext.Roles.Add(role);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _dbContext.Entry(role).State = EntityState.Detached;

            // role_code is the only unique constraint on role_master, so this is a duplicate code.
            throw new ConflictException($"A role with code '{role.RoleCode}' already exists.");
        }

        return role;
    }

    public async Task<Role> UpdateAsync(Role role, CancellationToken cancellationToken)
    {
        // The role came from GetByIdAsync (AsNoTracking), so it is not tracked yet. Attaching it
        // keeps the row_version it was loaded with as the ORIGINAL value, which is what EF puts in
        // the UPDATE's WHERE clause - that is the optimistic-concurrency check, using the existing
        // RowVersion configuration. Only the columns this operation owns are marked modified.
        var entry = _dbContext.Attach(role);
        entry.Property(r => r.RoleCode).IsModified = true;
        entry.Property(r => r.RoleName).IsModified = true;
        entry.Property(r => r.Description).IsModified = true;
        entry.Property(r => r.UpdatedAt).IsModified = true;
        entry.Property(r => r.UpdatedBy).IsModified = true;

        await SaveAsync(entry, role, cancellationToken);

        return role;
    }

    public async Task<Role> DeactivateAsync(Role role, CancellationToken cancellationToken)
    {
        // Same detached-entity / row_version approach as UpdateAsync, but the ONLY columns marked
        // modified are is_active, updated_at and updated_by - the UPDATE cannot touch anything
        // else, and no DELETE is ever issued.
        var entry = _dbContext.Attach(role);
        entry.Property(r => r.IsActive).IsModified = true;
        entry.Property(r => r.UpdatedAt).IsModified = true;
        entry.Property(r => r.UpdatedBy).IsModified = true;

        await SaveAsync(entry, role, cancellationToken);

        return role;
    }

    // Shared by every role write that goes through an attached, row_version-carrying entity.
    private async Task SaveAsync(EntityEntry<Role> entry, Role role, CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            entry.State = EntityState.Detached;

            throw new ConflictException(
                "This role was modified by another user after it was loaded. Reload the role and try again.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            entry.State = EntityState.Detached;

            // role_code is the only unique constraint on role_master, so this is a duplicate code.
            throw new ConflictException($"A role with code '{role.RoleCode}' already exists.");
        }
    }

    // 2601 = unique index violation, 2627 = unique/primary key constraint violation.
    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
