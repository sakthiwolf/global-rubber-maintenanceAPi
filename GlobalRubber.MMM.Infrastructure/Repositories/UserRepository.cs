using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

public sealed class UserRepository : IUserRepository
{
    private readonly GlobalRubberDbContext _dbContext;

    public UserRepository(GlobalRubberDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<(IReadOnlyList<User> Items, int TotalCount)> GetAllAsync(
        PaginationRequest request, CancellationToken cancellationToken)
    {
        var query = _dbContext.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .OrderBy(u => u.UserName);

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .Skip((request.PageNumber - 1) * request.PageSize)
            .Take(request.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }

    public Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken) =>
        _dbContext.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);

    public Task<User?> GetByLoginIdForAuthenticationAsync(string loginId, CancellationToken cancellationToken) =>
        _dbContext.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.LoginId == loginId, cancellationToken);

    public Task UpdateLastLoginAtAsync(int userId, DateTime lastLoginAtUtc, CancellationToken cancellationToken) =>
        _dbContext.Users
            .Where(u => u.UserId == userId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.LastLoginAt, lastLoginAtUtc), cancellationToken);
}
