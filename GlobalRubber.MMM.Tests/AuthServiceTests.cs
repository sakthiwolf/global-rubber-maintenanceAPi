using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Application.Services;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Tests;

/// <summary>Unit tests against the real AuthService with fake collaborators - no DB needed.</summary>
public class AuthServiceTests
{
    private static User ActiveUser => new()
    {
        UserId = 1,
        UserCode = "USR-0001",
        LoginId = "admin",
        UserName = "Administrator",
        PasswordHash = "hash-of-correct-password",
        RoleId = 1,
        Role = new Role { RoleId = 1, RoleCode = "ADMIN", RoleName = "Administrator" },
        IsActive = true,
        MustChangePassword = false,
    };

    private static User InactiveUser => new()
    {
        UserId = 2,
        UserCode = "USR-0002",
        LoginId = "olduser",
        UserName = "Former Employee",
        PasswordHash = "hash-of-correct-password",
        RoleId = 1,
        Role = new Role { RoleId = 1, RoleCode = "ADMIN", RoleName = "Administrator" },
        IsActive = false,
    };

    private static AuthService CreateService(User? existingUser, FakeAuditLogService? auditLogService = null) =>
        new(
            new FakeUserRepository(existingUser),
            new FakePasswordHasher("correct-password"),
            new FakeJwtTokenService(),
            new FixedDateTimeProvider(DateTime.UtcNow),
            auditLogService ?? new FakeAuditLogService(), new InMemoryRefreshTokenRepository(), new SequentialRefreshTokenGenerator());

    [Fact]
    public async Task LoginAsync_ReturnsAuthResponse_AndUpdatesLastLoginAt_ForValidCredentials()
    {
        var userRepository = new FakeUserRepository(ActiveUser);
        var dateTimeProvider = new FixedDateTimeProvider(new DateTime(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc));
        var service = new AuthService(userRepository, new FakePasswordHasher(correctPassword: "correct-password"),
            new FakeJwtTokenService(), dateTimeProvider, new FakeAuditLogService(), new InMemoryRefreshTokenRepository(), new SequentialRefreshTokenGenerator());

        var result = await service.LoginAsync(new LoginRequest { LoginId = "admin", Password = "correct-password" }, "203.0.113.5", CancellationToken.None);

        Assert.Equal("fake-jwt-token", result.Token);
        Assert.Equal("USR-0001", result.User.UserCode);
        Assert.Equal("Administrator", result.User.RoleName);
        Assert.Equal(dateTimeProvider.UtcNow, result.User.LastLoginAt);
        Assert.Equal(1, userRepository.LastLoginUpdateCount);
        Assert.Equal(dateTimeProvider.UtcNow, userRepository.LastRecordedLoginAtUtc);
    }

    [Fact]
    public async Task LoginAsync_Response_NeverExposesPasswordHash()
    {
        var service = CreateService(ActiveUser);

        var result = await service.LoginAsync(new LoginRequest { LoginId = "admin", Password = "correct-password" }, null, CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.DoesNotContain("passwordHash", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash-of-correct-password", json);
    }

    [Fact]
    public async Task LoginAsync_ThrowsValidationException_ForWrongPassword()
    {
        var service = CreateService(ActiveUser);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.LoginAsync(new LoginRequest { LoginId = "admin", Password = "wrong-password" }, null, CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_ThrowsValidationException_ForUnknownLoginId()
    {
        var service = CreateService(existingUser: null);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.LoginAsync(new LoginRequest { LoginId = "nobody", Password = "correct-password" }, null, CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_ThrowsValidationException_ForInactiveUser()
    {
        var service = CreateService(InactiveUser);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.LoginAsync(new LoginRequest { LoginId = "olduser", Password = "correct-password" }, null, CancellationToken.None));
    }

    [Fact]
    public async Task LoginAsync_UsesTheSameErrorMessage_ForUnknownLogin_WrongPassword_AndInactiveAccount()
    {
        var unknownEx = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService(null).LoginAsync(new LoginRequest { LoginId = "nobody", Password = "correct-password" }, null, CancellationToken.None));

        var wrongPasswordEx = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService(ActiveUser).LoginAsync(new LoginRequest { LoginId = "admin", Password = "wrong" }, null, CancellationToken.None));

        var inactiveEx = await Assert.ThrowsAsync<ValidationException>(() =>
            CreateService(InactiveUser).LoginAsync(new LoginRequest { LoginId = "olduser", Password = "correct-password" }, null, CancellationToken.None));

        Assert.Equal(unknownEx.Message, wrongPasswordEx.Message);
        Assert.Equal(unknownEx.Message, inactiveEx.Message);
    }

    [Fact]
    public async Task LoginAsync_ThrowsValidationException_ForMissingLoginIdOrPassword()
    {
        var service = CreateService(ActiveUser);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.LoginAsync(new LoginRequest { LoginId = "", Password = "correct-password" }, null, CancellationToken.None));

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.LoginAsync(new LoginRequest { LoginId = "admin", Password = "" }, null, CancellationToken.None));
    }

    // ---- Audit tests (Step 10) ----

    [Fact]
    public async Task LoginAsync_SuccessfulLogin_CreatesExactlyOneAuditEntry_WithCorrectIdentityAndAction()
    {
        var auditLogService = new FakeAuditLogService();
        var service = CreateService(ActiveUser, auditLogService);

        await service.LoginAsync(new LoginRequest { LoginId = "admin", Password = "correct-password" }, "203.0.113.5", CancellationToken.None);

        var entry = Assert.Single(auditLogService.LoggedEntries);
        Assert.Equal(1, entry.UserId);
        Assert.Equal("Administrator", entry.UserName);
        Assert.Equal("Security", entry.Module);
        Assert.Equal("Login", entry.Action);
        Assert.Equal("USR-0001", entry.RecordRef);
        Assert.Equal("203.0.113.5", entry.IpAddress);
    }

    [Fact]
    public async Task LoginAsync_SuccessfulLogin_AuditEntry_NeverContainsPasswordOrHashOrToken()
    {
        var auditLogService = new FakeAuditLogService();
        var service = CreateService(ActiveUser, auditLogService);

        await service.LoginAsync(new LoginRequest { LoginId = "admin", Password = "correct-password" }, null, CancellationToken.None);

        var entry = Assert.Single(auditLogService.LoggedEntries);
        var serialized = $"{entry.UserName}|{entry.Module}|{entry.Action}|{entry.EntityName}|{entry.RecordRef}|{entry.Description}|{entry.IpAddress}";
        Assert.DoesNotContain("correct-password", serialized);
        Assert.DoesNotContain("hash-of-correct-password", serialized);
        Assert.DoesNotContain("fake-jwt-token", serialized);
    }

    [Fact]
    public async Task LoginAsync_FailedLogin_CreatesAuditEntry_WithNullUserId_AndLoginIdInDescriptionOnly()
    {
        // Per the system analysis doc's own AuthService audit row: "failed login (UserId null,
        // LoginId in Description)".
        var auditLogService = new FakeAuditLogService();
        var service = CreateService(ActiveUser, auditLogService);

        await Assert.ThrowsAsync<ValidationException>(() =>
            service.LoginAsync(new LoginRequest { LoginId = "admin", Password = "wrong-password" }, "203.0.113.5", CancellationToken.None));

        var entry = Assert.Single(auditLogService.LoggedEntries);
        Assert.Null(entry.UserId);
        Assert.Equal("LoginFailed", entry.Action);
        Assert.Contains("admin", entry.Description);
        Assert.DoesNotContain("wrong-password", entry.Description);
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

    // ================================================================ refresh tokens (system analysis 17.5 / 18.1)

    private sealed record RefreshSut(AuthService Service, InMemoryRefreshTokenRepository Tokens, MutableClock Clock, FakeAuditLogService Audit);

    private sealed class MutableClock : IDateTimeProvider
    {
        public DateTime UtcNow { get; set; } = new(2026, 9, 25, 5, 0, 0, DateTimeKind.Utc);
        public DateOnly Today => DateOnly.FromDateTime(UtcNow);
    }

    private static RefreshSut CreateRefreshSut(User? user = null)
    {
        var tokens = new InMemoryRefreshTokenRepository();
        var clock = new MutableClock();
        var audit = new FakeAuditLogService();
        var service = new AuthService(new FakeUserRepository(user ?? ActiveUser), new FakePasswordHasher("correct-password"), new FakeJwtTokenService(),
            clock, audit, tokens, new SequentialRefreshTokenGenerator());
        return new RefreshSut(service, tokens, clock, audit);
    }

    private static Task<AuthResponseDto> Login(RefreshSut s) =>
        s.Service.LoginAsync(new LoginRequest { LoginId = "admin", Password = "correct-password" }, "10.0.0.5", CancellationToken.None);

    private static Task<AuthResponseDto> Refresh(RefreshSut s, string? token) =>
        s.Service.RefreshAsync(new RefreshTokenRequest { RefreshToken = token }, "10.0.0.6", CancellationToken.None);

    [Fact]
    public async Task Login_AlsoIssuesARefreshToken_StoredOnlyAsAHash_ValidFor7Days()
    {
        var s = CreateRefreshSut();

        var result = await Login(s);

        Assert.Equal("rt-1", result.RefreshToken);
        Assert.Equal(s.Clock.UtcNow.AddDays(7), result.RefreshTokenExpiresAtUtc);
        var stored = Assert.Single(s.Tokens.Tokens);
        Assert.Equal(("H(rt-1)", 1, "10.0.0.5"), (stored.TokenHash, stored.UserId, stored.CreatedByIp));
        Assert.DoesNotContain(s.Tokens.Tokens, t => t.TokenHash == "rt-1"); // never the raw token
    }

    [Fact]
    public async Task Refresh_AfterTheAccessTokenExpired_ReturnsANewPair_AndRotatesTheRefreshToken()
    {
        var s = CreateRefreshSut();
        var login = await Login(s);
        s.Clock.UtcNow = s.Clock.UtcNow.AddMinutes(40); // well past the 15-minute access token

        var refreshed = await Refresh(s, login.RefreshToken);

        Assert.Equal(("fake-jwt-token", "rt-2"), (refreshed.Token, refreshed.RefreshToken));
        Assert.Equal("admin", refreshed.User.LoginId);
        var old = s.Tokens.ByHash("H(rt-1)");
        Assert.Equal((s.Clock.UtcNow, "H(rt-2)", "10.0.0.6"), (old.RevokedAt!.Value, old.ReplacedByTokenHash, old.RevokedByIp));
        Assert.Null(s.Tokens.ByHash("H(rt-2)").RevokedAt);
        Assert.Equal(s.Clock.UtcNow.AddDays(7), refreshed.RefreshTokenExpiresAtUtc); // an active user's session keeps sliding
    }

    [Fact]
    public async Task ARefreshToken_WorksOnlyOnce()
    {
        var s = CreateRefreshSut();
        var login = await Login(s);
        await Refresh(s, login.RefreshToken);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Refresh(s, login.RefreshToken));
        Assert.Equal(2, s.Tokens.Tokens.Count); // no third token
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("never-issued")]
    public async Task AMissingOrUnknownRefreshToken_Is401(string? token)
    {
        var s = CreateRefreshSut();
        await Login(s);

        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Refresh(s, token));

        Assert.Equal("Your session has expired. Please sign in again.", ex.Message);
    }

    [Fact]
    public async Task AnExpiredRefreshToken_Is401()
    {
        var s = CreateRefreshSut();
        var login = await Login(s);
        s.Clock.UtcNow = s.Clock.UtcNow.AddDays(7).AddSeconds(1);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Refresh(s, login.RefreshToken));
    }

    [Fact]
    public async Task AUserDeactivatedAfterLogin_CannotRefresh_AndTheTokenIsRevoked()
    {
        var user = ActiveUser;
        var s = CreateRefreshSut(user);
        var login = await Login(s);
        user.IsActive = false;

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Refresh(s, login.RefreshToken));
        Assert.NotNull(s.Tokens.ByHash("H(rt-1)").RevokedAt);
    }

    [Fact]
    public async Task Logout_RevokesTheCallersRefreshToken_AndAuditsLogout()
    {
        var s = CreateRefreshSut();
        var login = await Login(s);
        s.Audit.LoggedEntries.Clear();

        await s.Service.LogoutAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken }, 1, "10.0.0.7", CancellationToken.None);

        Assert.Equal("10.0.0.7", s.Tokens.ByHash("H(rt-1)").RevokedByIp);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Refresh(s, login.RefreshToken));
        var entry = Assert.Single(s.Audit.LoggedEntries);
        Assert.Equal(("Logout", "Security", (int?)1), (entry.Action, entry.Module, entry.UserId));
    }

    [Fact]
    public async Task Logout_CannotRevokeAnotherUsersToken()
    {
        var s = CreateRefreshSut();
        var login = await Login(s);

        await s.Service.LogoutAsync(new RefreshTokenRequest { RefreshToken = login.RefreshToken }, 99, null, CancellationToken.None);

        Assert.Null(s.Tokens.ByHash("H(rt-1)").RevokedAt);
    }

    private sealed class FakePasswordHasher : IPasswordHasher
    {
        private readonly string _correctPassword;

        public FakePasswordHasher(string correctPassword)
        {
            _correctPassword = correctPassword;
        }

        public string HashPassword(string password) => $"hash-of-{password}";

        public bool VerifyPassword(string passwordHash, string providedPassword) =>
            providedPassword == _correctPassword;
    }

    private sealed class FakeJwtTokenService : IJwtTokenService
    {
        public (string Token, DateTime ExpiresAtUtc) GenerateAccessToken(User user) =>
            ("fake-jwt-token", new DateTime(2026, 1, 1, 0, 15, 0, DateTimeKind.Utc));
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        private readonly User? _existingUser;

        public FakeUserRepository(User? existingUser)
        {
            _existingUser = existingUser;
        }

        public int LastLoginUpdateCount { get; private set; }
        public DateTime? LastRecordedLoginAtUtc { get; private set; }

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by AuthServiceTests.");

        public Task<User?> GetByIdAsync(int userId, CancellationToken cancellationToken) =>
            Task.FromResult(_existingUser is not null && _existingUser.UserId == userId ? _existingUser : null);

        public Task<User?> GetByLoginIdForAuthenticationAsync(string loginId, CancellationToken cancellationToken) =>
            Task.FromResult(_existingUser is not null && _existingUser.LoginId == loginId ? _existingUser : null);

        public Task<bool> AnyActiveByRoleIdAsync(int roleId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed by AuthServiceTests.");

        public Task<bool> ExistsByLoginIdAsync(string loginId, int? excludeUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed here.");

        public Task<User> AddAsync(User user, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed here.");

        public Task<User> UpdateAsync(User user, byte[] originalRowVersion, bool passwordChanged, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not needed here.");

        public Task UpdateLastLoginAtAsync(int userId, DateTime lastLoginAtUtc, CancellationToken cancellationToken)
        {
            LastLoginUpdateCount++;
            LastRecordedLoginAtUtc = lastLoginAtUtc;
            return Task.CompletedTask;
        }
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

}
