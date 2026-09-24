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

    /// <summary>
    /// Whether at least one ACTIVE user is currently assigned to the role. A single existence
    /// query - no user rows or details are loaded - used to refuse deactivating a role that
    /// people still depend on.
    /// </summary>
    Task<bool> AnyActiveByRoleIdAsync(int roleId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether a user already has this login ID (case-insensitive - the login_id column's own collation
    /// handles it). <paramref name="excludeUserId"/> leaves one user out (the user being edited).
    /// </summary>
    Task<bool> ExistsByLoginIdAsync(string loginId, int? excludeUserId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the user and assigns its UserCode from the USER document sequence, both in ONE transaction:
    /// a failed insert (e.g. duplicate login ID) rolls the sequence increment back, so no number is burned.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The login ID already exists.</exception>
    Task<User> AddAsync(User user, CancellationToken cancellationToken);

    /// <summary>
    /// Saves LoginId, UserName, Email, Mobile, RoleId, IsActive, UpdatedAt and UpdatedBy (plus PasswordHash and
    /// MustChangePassword only when <paramref name="passwordChanged"/>) of a user loaded through
    /// <see cref="GetByIdAsync"/>. Nothing else is written - never UserCode, lockout state, LastLoginAt or the
    /// creation columns. <paramref name="originalRowVersion"/> (the version the caller last read) is the
    /// optimistic-concurrency guard: if the row changed since, the save is refused.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">
    /// The user was modified by someone else (or removed) since <paramref name="originalRowVersion"/>, or the login ID already exists.
    /// </exception>
    Task<User> UpdateAsync(User user, byte[] originalRowVersion, bool passwordChanged, CancellationToken cancellationToken);
}
