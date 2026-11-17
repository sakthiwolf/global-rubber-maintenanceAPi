using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// BR-31 applied to every request, not only to login and refresh: a user who is missing or inactive, or whose role is
/// inactive, is refused; otherwise the user's current role (which may differ from the one in the token, after a role
/// change) is what the request is authorized with.
/// </summary>
public sealed class SessionUserValidator : ISessionUserValidator
{
    private readonly IUserRepository _userRepository;

    public SessionUserValidator(IUserRepository userRepository)
    {
        _userRepository = userRepository;
    }

    public async Task<string?> GetCurrentRoleCodeAsync(int userId, string? tokenRoleCode, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null || !user.IsActive || user.Role is null || !user.Role.IsActive)
        {
            return null;
        }

        return user.Role.RoleCode;
    }
}
