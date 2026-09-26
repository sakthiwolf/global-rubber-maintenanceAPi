using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly GlobalRubberDbContext _dbContext;

    public RefreshTokenRepository(GlobalRubberDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task AddAsync(UserRefreshToken token, CancellationToken cancellationToken)
    {
        _dbContext.Set<UserRefreshToken>().Add(token);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.Entry(token).State = EntityState.Detached;
    }

    public Task<UserRefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        _dbContext.Set<UserRefreshToken>().AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public async Task<bool> RotateAsync(string oldTokenHash, UserRefreshToken replacement, DateTime nowUtc, string? ipAddress, CancellationToken cancellationToken)
    {
        var rotated = false;
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            rotated = false;
            var ownsTransaction = _dbContext.Database.CurrentTransaction is null;
            await using var transaction = ownsTransaction ? await _dbContext.Database.BeginTransactionAsync(cancellationToken) : null;

            // The conditional UPDATE is the gate: of two requests presenting the same token, only one sees a row to revoke.
            var revoked = await _dbContext.Set<UserRefreshToken>()
                .Where(t => t.TokenHash == oldTokenHash && t.RevokedAt == null && t.ExpiresAt > nowUtc)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.RevokedAt, nowUtc)
                    .SetProperty(t => t.ReplacedByTokenHash, replacement.TokenHash)
                    .SetProperty(t => t.RevokedByIp, ipAddress), cancellationToken);

            if (revoked != 1)
            {
                return; // nothing written; the (owned) transaction is rolled back on dispose
            }

            _dbContext.Set<UserRefreshToken>().Add(replacement);
            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                _dbContext.Entry(replacement).State = EntityState.Detached;
            }

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            rotated = true;
        });

        return rotated;
    }

    public async Task<bool> RevokeAsync(string tokenHash, int userId, DateTime nowUtc, string? ipAddress, CancellationToken cancellationToken) =>
        await _dbContext.Set<UserRefreshToken>()
            .Where(t => t.TokenHash == tokenHash && t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s
                .SetProperty(t => t.RevokedAt, nowUtc)
                .SetProperty(t => t.RevokedByIp, ipAddress), cancellationToken) == 1;
}
