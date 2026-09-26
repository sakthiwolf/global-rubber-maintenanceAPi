namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to security.user_refresh_token (script 003): the rotating refresh token that keeps a signed-in user's session
/// alive past the short access-token lifetime (system analysis 17.5: "JWT access token (about 15 min) plus a rotating
/// refresh token (7 days) stored hashed"). Only the SHA-256 hash of the token is stored - never the raw token.
/// A token is usable once: refreshing revokes it and records the hash of its replacement (ReplacedByTokenHash).
/// </summary>
public class UserRefreshToken
{
    public long RefreshTokenId { get; set; }
    public int UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public string? CreatedByIp { get; set; }
    public string? RevokedByIp { get; set; }
}
