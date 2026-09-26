namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>Creates cryptographically random refresh tokens and hashes them for storage (the raw token is never stored).</summary>
public interface IRefreshTokenGenerator
{
    /// <summary>A new raw token (returned to the client once), its hash (stored) and its expiry.</summary>
    (string Token, string TokenHash, DateTime ExpiresAtUtc) Create(DateTime nowUtc);

    /// <summary>The storage hash of a raw token presented by a client.</summary>
    string Hash(string token);
}
