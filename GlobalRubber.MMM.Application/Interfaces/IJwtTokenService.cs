using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IJwtTokenService
{
    /// <summary>
    /// Generates a signed JWT access token for the given user. <paramref name="user"/> must
    /// have its Role navigation loaded - the role code becomes the token's role claim.
    /// Returns the token together with its expiry (UTC) so the caller does not need its own
    /// copy of the configured token lifetime.
    /// </summary>
    (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(User user);
}
