using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IUserService
{
    Task<PagedResult<UserDto>> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No user exists with the given id.</exception>
    Task<UserDto> GetByIdAsync(int userId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a user with a hashed password and a code issued from the USER document sequence.
    /// <paramref name="actingUserId"/> / <paramref name="ipAddress"/> come from the controller and feed
    /// CreatedBy and the UserCreated audit entry only.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, or the role is not active.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">The selected role does not exist.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The login ID already exists.</exception>
    Task<UserDto> CreateAsync(CreateUserRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the editable profile fields (and, only if supplied, the password). Guarded by the caller's
    /// row version. Writes UserUpdated, plus UserRoleChanged when the role changes.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">The user, or the newly selected role, does not exist.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">A field fails validation, the row version is missing/invalid, or the new role is not active.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The login ID belongs to another user, or the user was modified by someone else after it was loaded.</exception>
    Task<UserDto> UpdateAsync(int userId, UpdateUserRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
