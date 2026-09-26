using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

/// <summary>security.user_refresh_token - looked up and changed by the token's HASH only.</summary>
public interface IRefreshTokenRepository
{
    Task AddAsync(UserRefreshToken token, CancellationToken cancellationToken);

    Task<UserRefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically revokes the token with <paramref name="oldTokenHash"/> (only if it is still unrevoked and unexpired at
    /// <paramref name="nowUtc"/>) and stores <paramref name="replacement"/>. False - and nothing written - when another
    /// request has already used or revoked it, so a refresh token can be exchanged exactly once.
    /// </summary>
    Task<bool> RotateAsync(string oldTokenHash, UserRefreshToken replacement, DateTime nowUtc, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Revokes the user's token with this hash if it is still active. False when there was nothing to revoke.</summary>
    Task<bool> RevokeAsync(string tokenHash, int userId, DateTime nowUtc, string? ipAddress, CancellationToken cancellationToken);
}
