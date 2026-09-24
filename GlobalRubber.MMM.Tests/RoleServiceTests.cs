using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Unit tests of the real <see cref="RoleService"/> against fake repositories/collaborators - the
/// real RoleRepository needs a live SQL Server connection this environment does not have.
/// </summary>
public partial class RoleServiceTests
{
    [Fact]
    public async Task GetAllAsync_ReturnsPagedResult_MappedFromRepository()
    {
        var service = CreateService(SampleRoles());

        var result = await service.GetAllAsync(
            new PaginationRequest { PageNumber = 1, PageSize = 10 }, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, r => r.RoleCode == "ADMIN" && r.RoleName == "Administrator");
    }

    [Fact]
    public async Task GetAllAsync_RespectsPageSize()
    {
        var service = CreateService(SampleRoles());

        var result = await service.GetAllAsync(
            new PaginationRequest { PageNumber = 1, PageSize = 1 }, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task GetByIdAsync_ReturnsMappedRole_WhenFound()
    {
        var service = CreateService(SampleRoles());

        var dto = await service.GetByIdAsync(1, CancellationToken.None);

        Assert.Equal("ADMIN", dto.RoleCode);
        Assert.Equal("Administrator", dto.RoleName);
        Assert.True(dto.IsSystemRole);
    }

    [Fact]
    public async Task GetByIdAsync_ThrowsNotFoundException_WhenRoleDoesNotExist()
    {
        var service = CreateService(SampleRoles());

        await Assert.ThrowsAsync<NotFoundException>(() => service.GetByIdAsync(999, CancellationToken.None));
    }

    private static RoleService CreateService(
        List<Role> roles, IAuditLogService? auditLogService = null, IUserRepository? userRepository = null) =>
        CreateService(new FakeRoleRepository(roles), auditLogService, userRepository);

    private static RoleService CreateService(
        FakeRoleRepository roleRepository, IAuditLogService? auditLogService = null, IUserRepository? userRepository = null) =>
        new(
            roleRepository,
            userRepository ?? new FakeUserRepository(),
            new FixedDateTimeProvider(new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc)),
            auditLogService ?? new FakeAuditLogService(),
            NullLogger<RoleService>.Instance);

    private static List<Role> SampleRoles() => new()
    {
        new Role { RoleId = 1, RoleCode = "ADMIN", RoleName = "Administrator", IsSystemRole = true, IsActive = true },
        new Role { RoleId = 2, RoleCode = "MAINT_MANAGER", RoleName = "Maintenance Manager", IsSystemRole = false, IsActive = true },
    };

    /// <summary>
    /// Behaves like the real RoleRepository where it matters for the update tests: GetByIdAsync
    /// hands out a detached COPY carrying the row version it was read with, and UpdateAsync only
    /// succeeds if that version still matches the stored row (then bumps it) - so a modification
    /// made in between is refused with the same ConflictException the real repository throws.
    /// </summary>
    private sealed class FakeRoleRepository : IRoleRepository
    {
        private readonly List<Role> _roles;

        public FakeRoleRepository(List<Role> roles)
        {
            _roles = roles;
        }

        /// <summary>Runs at the start of UpdateAsync - lets a test play "another user saved first".</summary>
        public Action<int>? BeforeUpdate { get; set; }

        public int UpdateCallCount { get; private set; }

        public int DeactivateCallCount { get; private set; }

        /// <summary>The stored row (not a copy) - for assertions on what was actually persisted.</summary>
        public Role Stored(int roleId) => _roles.Single(r => r.RoleId == roleId);

        /// <summary>Simulates a concurrent writer: changes the stored row's version.</summary>
        public void SimulateConcurrentModification(int roleId) => Stored(roleId).RowVersion = NextVersion(Stored(roleId).RowVersion);

        public Task<(IReadOnlyList<Role> Items, int TotalCount)> GetAllAsync(
            PaginationRequest request, CancellationToken cancellationToken)
        {
            var items = _roles
                .Skip((request.PageNumber - 1) * request.PageSize)
                .Take(request.PageSize)
                .ToList();

            return Task.FromResult<(IReadOnlyList<Role>, int)>((items, _roles.Count));
        }

        public Task<Role?> GetByIdAsync(int roleId, CancellationToken cancellationToken)
        {
            var stored = _roles.FirstOrDefault(r => r.RoleId == roleId);

            return Task.FromResult(stored is null ? null : Clone(stored));
        }

        public Task<bool> ExistsByCodeAsync(string roleCode, int? excludeRoleId, CancellationToken cancellationToken) =>
            Task.FromResult(_roles.Any(r => r.RoleCode == roleCode && r.RoleId != excludeRoleId));

        public Task<bool> ExistsByNameAsync(string roleName, int? excludeRoleId, CancellationToken cancellationToken) =>
            Task.FromResult(_roles.Any(r =>
                string.Equals(r.RoleName, roleName, StringComparison.OrdinalIgnoreCase) && r.RoleId != excludeRoleId));

        public Task<Role> AddAsync(Role role, CancellationToken cancellationToken)
        {
            role.RoleId = _roles.Count == 0 ? 1 : _roles.Max(r => r.RoleId) + 1;
            role.RowVersion = NextVersion(Array.Empty<byte>());
            _roles.Add(role);
            return Task.FromResult(role);
        }

        public Task<Role> UpdateAsync(Role role, CancellationToken cancellationToken)
        {
            UpdateCallCount++;
            BeforeUpdate?.Invoke(role.RoleId);

            var stored = _roles.FirstOrDefault(r => r.RoleId == role.RoleId);
            if (stored is null || !stored.RowVersion.SequenceEqual(role.RowVersion))
            {
                throw new ConflictException(
                    "This role was modified by another user after it was loaded. Reload the role and try again.");
            }

            // Only what the real UpdateAsync writes - IsSystemRole/IsActive/CreatedAt/CreatedBy are not touched.
            stored.RoleCode = role.RoleCode;
            stored.RoleName = role.RoleName;
            stored.Description = role.Description;
            stored.UpdatedAt = role.UpdatedAt;
            stored.UpdatedBy = role.UpdatedBy;
            stored.RowVersion = NextVersion(stored.RowVersion);
            role.RowVersion = stored.RowVersion;

            return Task.FromResult(role);
        }

        public Task<Role> DeactivateAsync(Role role, CancellationToken cancellationToken)
        {
            DeactivateCallCount++;
            BeforeUpdate?.Invoke(role.RoleId);

            var stored = _roles.FirstOrDefault(r => r.RoleId == role.RoleId);
            if (stored is null || !stored.RowVersion.SequenceEqual(role.RowVersion))
            {
                throw new ConflictException(
                    "This role was modified by another user after it was loaded. Reload the role and try again.");
            }

            // Only what the real DeactivateAsync writes: is_active, updated_at, updated_by.
            stored.IsActive = role.IsActive;
            stored.UpdatedAt = role.UpdatedAt;
            stored.UpdatedBy = role.UpdatedBy;
            stored.RowVersion = NextVersion(stored.RowVersion);
            role.RowVersion = stored.RowVersion;

            return Task.FromResult(role);
        }

        private static byte[] NextVersion(byte[] current) => new[] { (byte)(current.Length == 0 ? 1 : current[0] + 1) };

        private static Role Clone(Role r) => new()
        {
            RoleId = r.RoleId,
            RoleCode = r.RoleCode,
            RoleName = r.RoleName,
            Description = r.Description,
            IsSystemRole = r.IsSystemRole,
            IsActive = r.IsActive,
            CreatedAt = r.CreatedAt,
            CreatedBy = r.CreatedBy,
            UpdatedAt = r.UpdatedAt,
            UpdatedBy = r.UpdatedBy,
            RowVersion = (byte[])r.RowVersion.Clone(),
        };
    }

    private sealed class FixedDateTimeProvider : IDateTimeProvider
    {
        public FixedDateTimeProvider(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; }
        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }

    private sealed class FakeAuditLogService : IAuditLogService
    {
        public List<AuditLogEntry> LoggedEntries { get; } = new();

        public Task LogAsync(AuditLogEntry entry, CancellationToken cancellationToken)
        {
            LoggedEntries.Add(entry);
            return Task.CompletedTask;
        }
    }

    /// <summary>The real AuditLogService (which swallows persistence failures) over a repository that always fails.</summary>
    private static AuditLogService AuditServiceWhoseDatabaseFails() =>
        new(new ThrowingAuditLogRepository(),
            new FixedDateTimeProvider(new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc)),
            NullLogger<AuditLogService>.Instance);

    private sealed class ThrowingAuditLogRepository : IAuditLogRepository
    {
        public Task AddAsync(AuditLog auditLog, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated audit DB failure.");
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly bool _lookupFails;
        private readonly HashSet<int> _roleIdsWithActiveUsers;

        public FakeUserRepository(bool lookupFails = false, IEnumerable<int>? roleIdsWithActiveUsers = null)
        {
            _lookupFails = lookupFails;
            _roleIdsWithActiveUsers = roleIdsWithActiveUsers?.ToHashSet() ?? new HashSet<int>();
        }

        public Task<bool> AnyActiveByRoleIdAsync(int roleId, CancellationToken cancellationToken) =>
            Task.FromResult(_roleIdsWithActiveUsers.Contains(roleId));

        public Task<bool> ExistsByLoginIdAsync(string loginId, int? excludeUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed here.");

        public Task<User> AddAsync(User user, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed here.");

        public Task<User> UpdateAsync(User user, byte[] originalRowVersion, bool passwordChanged, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed here.");

        // One known user (id 7) - enough to prove the audit entry's UserName is resolved.
        public Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken) =>
            _lookupFails
                ? throw new InvalidOperationException("Simulated user lookup failure.")
                : Task.FromResult<User?>(userId == 7 ? new User { UserId = 7, UserName = "Francis Xavier" } : null);

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by RoleServiceTests.");

        public Task<User?> GetByLoginIdForAuthenticationAsync(string loginId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by RoleServiceTests.");

        public Task UpdateLastLoginAtAsync(int userId, DateTime lastLoginAtUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by RoleServiceTests.");
    }
}
