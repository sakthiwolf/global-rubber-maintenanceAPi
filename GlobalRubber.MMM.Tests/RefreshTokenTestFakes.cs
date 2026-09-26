using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>Deterministic refresh tokens: "rt-1", "rt-2", ... hashed as "H(rt-1)"; lifetime 7 days.</summary>
internal sealed class SequentialRefreshTokenGenerator : IRefreshTokenGenerator
{
    private int _next;

    public (string Token, string TokenHash, DateTime ExpiresAtUtc) Create(DateTime nowUtc)
    {
        var token = $"rt-{++_next}";
        return (token, Hash(token), nowUtc.AddDays(7));
    }

    public string Hash(string token) => $"H({token})";
}

/// <summary>Behaves like RefreshTokenRepository: rotation revokes the old token only if still active, then adds the new one.</summary>
internal sealed class InMemoryRefreshTokenRepository : IRefreshTokenRepository
{
    public List<UserRefreshToken> Tokens { get; } = new();

    public UserRefreshToken ByHash(string hash) => Tokens.Single(t => t.TokenHash == hash);

    public Task AddAsync(UserRefreshToken token, CancellationToken cancellationToken)
    {
        token.RefreshTokenId = Tokens.Count + 1;
        Tokens.Add(token);
        return Task.CompletedTask;
    }

    public Task<UserRefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        var t = Tokens.FirstOrDefault(x => x.TokenHash == tokenHash);
        return Task.FromResult(t is null ? null : new UserRefreshToken
        {
            RefreshTokenId = t.RefreshTokenId, UserId = t.UserId, TokenHash = t.TokenHash, ExpiresAt = t.ExpiresAt, CreatedAt = t.CreatedAt,
            RevokedAt = t.RevokedAt, ReplacedByTokenHash = t.ReplacedByTokenHash, CreatedByIp = t.CreatedByIp, RevokedByIp = t.RevokedByIp,
        });
    }

    public async Task<bool> RotateAsync(string oldTokenHash, UserRefreshToken replacement, DateTime nowUtc, string? ipAddress, CancellationToken cancellationToken)
    {
        var old = Tokens.FirstOrDefault(t => t.TokenHash == oldTokenHash && t.RevokedAt == null && t.ExpiresAt > nowUtc);
        if (old is null)
        {
            return false;
        }

        old.RevokedAt = nowUtc;
        old.ReplacedByTokenHash = replacement.TokenHash;
        old.RevokedByIp = ipAddress;
        await AddAsync(replacement, cancellationToken);
        return true;
    }

    public Task<bool> RevokeAsync(string tokenHash, int userId, DateTime nowUtc, string? ipAddress, CancellationToken cancellationToken)
    {
        var t = Tokens.FirstOrDefault(x => x.TokenHash == tokenHash && x.UserId == userId && x.RevokedAt == null);
        if (t is null)
        {
            return Task.FromResult(false);
        }

        t.RevokedAt = nowUtc;
        t.RevokedByIp = ipAddress;
        return Task.FromResult(true);
    }
}
