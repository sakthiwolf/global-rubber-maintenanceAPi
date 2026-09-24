using System.Security.Cryptography;
using System.Text;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>Shared fakes for the user-management tests (service level and HTTP level).</summary>
internal static class UserTestData
{
    // id 1 ADMIN (system, active), 2 MAINT_MANAGER (active), 3 TEST (active, created dynamically), 4 OLD_ROLE (inactive)
    public static List<Role> Roles() => new()
    {
        new Role { RoleId = 1, RoleCode = "ADMIN", RoleName = "Administrator", IsSystemRole = true, IsActive = true },
        new Role { RoleId = 2, RoleCode = "MAINT_MANAGER", RoleName = "Maintenance Manager", IsActive = true },
        new Role { RoleId = 3, RoleCode = "TEST", RoleName = "test", IsActive = true },
        new Role { RoleId = 4, RoleCode = "OLD_ROLE", RoleName = "Retired Role", IsActive = false },
    };

    // user 1 = the acting admin, user 2 = an ordinary user with a known hash / lockout state.
    public static List<User> Users() => new()
    {
        new User
        {
            UserId = 1, UserCode = "USR-0001", LoginId = "Admin", UserName = "Sakthi", RoleId = 1,
            PasswordHash = Sha256Hasher.Hash("admin-pass"), MustChangePassword = false, IsActive = true,
            CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), RowVersion = new byte[] { 1 },
        },
        new User
        {
            UserId = 2, UserCode = "USR-0002", LoginId = "ravi", UserName = "Ravi Kumar", Email = "ravi@example.com", Mobile = "9840011122",
            RoleId = 2, PasswordHash = Sha256Hasher.Hash("ravi-pass"), MustChangePassword = false, IsActive = true,
            FailedLoginCount = 2, LastLoginAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
            CreatedAt = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc), CreatedBy = 1, RowVersion = new byte[] { 1 },
        },
    };
}

/// <summary>Deterministic, non-reversible stand-in for the real hasher: the hash never contains the plaintext.</summary>
internal sealed class Sha256Hasher : IPasswordHasher
{
    public List<string> Hashed { get; } = new();

    public static string Hash(string password) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password)));

    public string HashPassword(string password)
    {
        Hashed.Add(password);
        return Hash(password);
    }

    public bool VerifyPassword(string passwordHash, string providedPassword) => passwordHash == Hash(providedPassword);
}

internal sealed class RecordingAuditLog : IAuditLogService
{
    public List<AuditLogEntry> Entries { get; } = new();

    public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }
}

internal sealed class FixedClock : IDateTimeProvider
{
    public static readonly DateTime Now = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);
    public DateTime UtcNow => Now;
    public DateOnly Today => DateOnly.FromDateTime(Now);
}

internal sealed class SimpleRoleRepository : IRoleRepository
{
    private readonly List<Role> _roles;

    public SimpleRoleRepository(List<Role> roles)
    {
        _roles = roles;
    }

    public Role? Find(int roleId) => _roles.FirstOrDefault(r => r.RoleId == roleId);

    public Task<Role?> GetByIdAsync(int roleId, CancellationToken cancellationToken) =>
        Task.FromResult(_roles.FirstOrDefault(r => r.RoleId == roleId) is { } r
            ? new Role { RoleId = r.RoleId, RoleCode = r.RoleCode, RoleName = r.RoleName, IsSystemRole = r.IsSystemRole, IsActive = r.IsActive }
            : null);

    public Task<(IReadOnlyList<Role> Items, int TotalCount)> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken) =>
        throw new NotSupportedException();

    public Task<bool> ExistsByCodeAsync(string roleCode, int? excludeRoleId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<bool> ExistsByNameAsync(string roleName, int? excludeRoleId, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<Role> AddAsync(Role role, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<Role> UpdateAsync(Role role, CancellationToken cancellationToken) => throw new NotSupportedException();
    public Task<Role> DeactivateAsync(Role role, CancellationToken cancellationToken) => throw new NotSupportedException();
}

/// <summary>
/// Behaves like the real UserRepository where it matters: reads hand out detached copies (with the Role
/// attached), AddAsync issues the next USR-NNNN code and refuses a duplicate login ID, and UpdateAsync only
/// writes the columns the real one writes - and only if the caller's row version still matches.
/// </summary>
internal sealed class InMemoryUserRepository : IUserRepository
{
    private readonly List<User> _users;
    private readonly Func<int, Role?> _roleLookup;
    private int _nextCode;
    private int _getByIdCalls;

    public InMemoryUserRepository(List<User> users, Func<int, Role?> roleLookup)
    {
        _users = users;
        _roleLookup = roleLookup;
        _nextCode = users.Count + 1;
    }

    /// <summary>Runs at the start of UpdateAsync - lets a test play "another user saved first".</summary>
    public Action<int>? BeforeUpdate { get; set; }
    /// <summary>When set, GetByIdAsync throws on that call number (1-based) - to test the audit user-name lookup failing.</summary>
    public int? ThrowOnGetByIdCall { get; set; }
    public int AddCalls { get; private set; }
    public int UpdateCalls { get; private set; }
    public int Count => _users.Count;

    public User Stored(int userId) => _users.Single(u => u.UserId == userId);
    public User StoredByLogin(string loginId) => _users.Single(u => string.Equals(u.LoginId, loginId, StringComparison.OrdinalIgnoreCase));
    public void SimulateConcurrentModification(int userId) => Stored(userId).RowVersion = Next(Stored(userId).RowVersion);

    private User Clone(User u) => new()
    {
        UserId = u.UserId, UserCode = u.UserCode, LoginId = u.LoginId, UserName = u.UserName, PasswordHash = u.PasswordHash,
        RoleId = u.RoleId, EmployeeId = u.EmployeeId, DepartmentId = u.DepartmentId, Email = u.Email, Mobile = u.Mobile,
        MustChangePassword = u.MustChangePassword, FailedLoginCount = u.FailedLoginCount, LockoutEndAt = u.LockoutEndAt,
        LastLoginAt = u.LastLoginAt, IsActive = u.IsActive, CreatedAt = u.CreatedAt, CreatedBy = u.CreatedBy,
        UpdatedAt = u.UpdatedAt, UpdatedBy = u.UpdatedBy, RowVersion = (byte[])u.RowVersion.Clone(),
        Role = _roleLookup(u.RoleId)!,
    };

    private static byte[] Next(byte[] current) => new[] { (byte)(current.Length == 0 ? 1 : current[0] + 1) };

    public Task<(IReadOnlyList<User> Items, int TotalCount)> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken)
    {
        var items = _users.OrderBy(u => u.UserName).Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).Select(Clone).ToList();
        return Task.FromResult<(IReadOnlyList<User>, int)>((items, _users.Count));
    }

    public Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken)
    {
        if (ThrowOnGetByIdCall == ++_getByIdCalls)
        {
            throw new InvalidOperationException("Simulated user lookup failure.");
        }

        var stored = _users.FirstOrDefault(u => u.UserId == userId);
        return Task.FromResult(stored is null ? null : Clone(stored));
    }

    public Task<User?> GetByLoginIdForAuthenticationAsync(string loginId, CancellationToken cancellationToken)
    {
        var stored = _users.FirstOrDefault(u => string.Equals(u.LoginId, loginId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(stored is null ? null : Clone(stored));
    }

    public Task UpdateLastLoginAtAsync(int userId, DateTime lastLoginAtUtc, CancellationToken cancellationToken)
    {
        Stored(userId).LastLoginAt = lastLoginAtUtc;
        return Task.CompletedTask;
    }

    public Task<bool> AnyActiveByRoleIdAsync(int roleId, CancellationToken cancellationToken) =>
        Task.FromResult(_users.Any(u => u.RoleId == roleId && u.IsActive));

    public Task<bool> ExistsByLoginIdAsync(string loginId, int? excludeUserId, CancellationToken cancellationToken) =>
        Task.FromResult(_users.Any(u => string.Equals(u.LoginId, loginId, StringComparison.OrdinalIgnoreCase) && u.UserId != excludeUserId));

    public Task<User> AddAsync(User user, CancellationToken cancellationToken)
    {
        AddCalls++;
        if (_users.Any(u => string.Equals(u.LoginId, user.LoginId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException($"A user with login ID '{user.LoginId}' already exists.");
        }

        user.UserId = _users.Max(u => u.UserId) + 1;
        user.UserCode = $"USR-{_nextCode++:0000}"; // the real repository issues this from the USER document sequence
        user.RowVersion = new byte[] { 1 };
        _users.Add(Clone(user));
        return Task.FromResult(user);
    }

    public Task<User> UpdateAsync(User user, byte[] originalRowVersion, bool passwordChanged, CancellationToken cancellationToken)
    {
        UpdateCalls++;
        BeforeUpdate?.Invoke(user.UserId);

        var stored = _users.FirstOrDefault(u => u.UserId == user.UserId);
        if (stored is null || !stored.RowVersion.SequenceEqual(originalRowVersion))
        {
            throw new ConflictException("The user was modified by another user. Refresh the user and try again.");
        }

        if (_users.Any(u => u.UserId != user.UserId && string.Equals(u.LoginId, user.LoginId, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ConflictException($"A user with login ID '{user.LoginId}' already exists.");
        }

        // Exactly the columns the real UpdateAsync writes - never UserCode, lockout state, LastLoginAt, creation columns.
        stored.LoginId = user.LoginId;
        stored.UserName = user.UserName;
        stored.Email = user.Email;
        stored.Mobile = user.Mobile;
        stored.RoleId = user.RoleId;
        stored.IsActive = user.IsActive;
        stored.UpdatedAt = user.UpdatedAt;
        stored.UpdatedBy = user.UpdatedBy;
        if (passwordChanged)
        {
            stored.PasswordHash = user.PasswordHash;
            stored.MustChangePassword = user.MustChangePassword;
        }

        stored.RowVersion = Next(stored.RowVersion);
        user.RowVersion = stored.RowVersion;
        return Task.FromResult(user);
    }
}
