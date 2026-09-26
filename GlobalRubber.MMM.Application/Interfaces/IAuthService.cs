using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IAuthService
{
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">
    /// LoginId/Password missing, the credentials are invalid, or the account is inactive - one
    /// generic message covers all three, so a failure never reveals which case occurred.
    /// </exception>
    /// <param name="ipAddress">Caller's IP, for the audit entry only - the Application layer has
    /// no access to HttpContext, so the controller extracts and passes this in.</param>
    Task<AuthResponseDto> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges a valid refresh token for a new access token AND a new refresh token (rotation - the presented one is
    /// revoked). The user must still exist and be active.
    /// </summary>
    /// <exception cref="UnauthorizedAccessException">Missing, unknown, expired, revoked or already-used token, or an
    /// inactive/removed user - one generic 401 so the client simply signs in again.</exception>
    Task<AuthResponseDto> RefreshAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>Revokes the signed-in user's refresh token (if it is theirs and still active) and audits the logout.</summary>
    Task LogoutAsync(RefreshTokenRequest request, int userId, string? ipAddress, CancellationToken cancellationToken);
}
