namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>
/// Login response. Deliberately no separate top-level "role" field, unlike the loosely
/// conceptual { token, user, role } shape sometimes sketched for this kind of endpoint -
/// UserDto already carries RoleId/RoleName, so a sibling "role" object would just duplicate it.
/// mustChangePassword is exposed the same way, via User.MustChangePassword - not duplicated.
/// </summary>
public sealed class AuthResponseDto
{
    public string Token { get; init; } = string.Empty;
    public DateTime ExpiresAtUtc { get; init; }

    /// <summary>
    /// The rotating refresh token (raw - returned only here, stored only as a hash). Exchange it at POST /auth/refresh for a
    /// new access token before/after the access token expires; each refresh token works once.
    /// </summary>
    public string RefreshToken { get; init; } = string.Empty;
    public DateTime RefreshTokenExpiresAtUtc { get; init; }
    public UserDto User { get; init; } = null!;
}
