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
    public UserDto User { get; init; } = null!;
}
