using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IUserRepository
{
    Task<(IReadOnlyList<User> Items, int TotalCount)> GetAllAsync(
        PaginationRequest request, CancellationToken cancellationToken);

    Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken);

    /// <summary>
    /// Loads a user by LoginId (case-insensitive - the login_id column's own collation handles
    /// this, no extra code needed) with Role included, for authentication only. Includes
    /// PasswordHash - callers other than AuthService must never surface it through UserDto or
    /// any API response.
    /// </summary>
    Task<User?> GetByLoginIdForAuthenticationAsync(string loginId, CancellationToken cancellationToken);

    /// <summary>
    /// Narrow, single-column update, deliberately not routed through a tracked entity + full
    /// SaveChanges: a login timestamp should not participate in row_version optimistic
    /// concurrency the way a real data edit (e.g. permissions) does.
    /// </summary>
    Task UpdateLastLoginAtAsync(int userId, DateTime lastLoginAtUtc, CancellationToken cancellationToken);
}
