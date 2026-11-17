namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// Re-checks the user behind an access token on every authenticated request, so a deactivated user - or one whose role
/// was changed or deactivated - loses (or changes) access at once instead of when the token expires.
/// </summary>
public interface ISessionUserValidator
{
    /// <summary>
    /// The user's CURRENT role code when the user exists, is active and has an active role; null otherwise (the token
    /// must then be rejected). <paramref name="tokenRoleCode"/> is the role claim the token carries.
    /// </summary>
    Task<string?> GetCurrentRoleCodeAsync(int userId, string? tokenRoleCode, CancellationToken cancellationToken);
}
