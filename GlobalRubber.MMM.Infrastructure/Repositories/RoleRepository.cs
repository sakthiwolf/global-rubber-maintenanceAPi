using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

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
}
