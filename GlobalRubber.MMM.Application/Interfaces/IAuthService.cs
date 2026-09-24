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
}
