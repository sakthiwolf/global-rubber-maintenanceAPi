using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// BR-31 (system analysis): login id is case-insensitive (handled by the column's own
/// collation - see IUserRepository.GetByLoginIdForAuthenticationAsync) and inactive users
/// cannot log in. Deliberately does NOT touch failed_login_count/lockout_end_at: the actual
/// lockout policy (attempt threshold, lockout duration) is an open question in this project's
/// own docs (Open Questions Q-37 - "lockout threshold... not yet defined"), so no lockout logic
/// is implemented here rather than inventing a threshold. Does NOT put permission data in the
/// token or response - that is Step 8 (authorization).
///
/// Audits both outcomes, per the system analysis doc's own AuthService audit row (section 20):
/// "Login success, failed login (UserId null, LoginId in Description), logout, password
/// change, lockout, role/permission change" - failed login is explicitly required there, not
/// invented here.
///
/// Sessions (system analysis 17.5 / 18.1): login returns a short-lived JWT access token (Jwt:ExpirationMinutes) plus a
/// rotating refresh token (Jwt:RefreshTokenDays), stored only as a hash in security.user_refresh_token. POST
/// /auth/refresh exchanges it - once - for a new pair, so an active user stays signed in; POST /auth/logout revokes it.
/// </summary>
public sealed class AuthService : IAuthService
{
    private const string InvalidCredentialsMessage = "Invalid login ID or password.";
    private const string SecurityModule = "Security"; // matches the analysis doc's own "Event class" column
    private const string SessionExpiredMessage = "Your session has expired. Please sign in again.";

    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IRefreshTokenGenerator _refreshTokenGenerator;

    public AuthService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        IRefreshTokenRepository refreshTokenRepository,
        IRefreshTokenGenerator refreshTokenGenerator)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _refreshTokenRepository = refreshTokenRepository;
        _refreshTokenGenerator = refreshTokenGenerator;
    }

    public async Task<AuthResponseDto> LoginAsync(LoginRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var loginId = request.LoginId.Trim();
        var password = request.Password;

        if (loginId.Length == 0 || password.Length == 0)
        {
            throw new ValidationException("Login ID and password are required.");
        }

        var user = await _userRepository.GetByLoginIdForAuthenticationAsync(loginId, cancellationToken);

        var passwordValid = user is not null && _passwordHasher.VerifyPassword(user.PasswordHash, password);

        // Same generic message whether the login id doesn't exist, the password is wrong, or
        // the account is inactive - a failure must never reveal which of those happened.
        if (user is null || !passwordValid || !user.IsActive)
        {
            await _auditLogService.LogAsync(new AuditLogEntry
            {
                UserId = null,
                UserName = "Unknown",
                Module = SecurityModule,
                Action = "LoginFailed",
                Description = $"Failed login attempt for login ID '{loginId}'.",
                IpAddress = ipAddress,
            }, cancellationToken);

            throw new ValidationException(InvalidCredentialsMessage);
        }

        var now = _dateTimeProvider.UtcNow;
        await _userRepository.UpdateLastLoginAtAsync(user.UserId, now, cancellationToken);

        // The entity was loaded before the update above (and AsNoTracking, so it was never
        // going to reflect it automatically) - set it locally so the response's LastLoginAt
        // matches what was just persisted, not the previous login.
        user.LastLoginAt = now;

        var (token, expiresAtUtc) = _jwtTokenService.GenerateAccessToken(user);
        var (refreshToken, refreshTokenHash, refreshExpiresAtUtc) = _refreshTokenGenerator.Create(now);
        await _refreshTokenRepository.AddAsync(new UserRefreshToken
        {
            UserId = user.UserId, TokenHash = refreshTokenHash, ExpiresAt = refreshExpiresAtUtc, CreatedAt = now, CreatedByIp = ipAddress,
        }, cancellationToken);

        // Authentication -> password verified -> JWT generated -> audit login success -> return.
        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = user.UserId,
            UserName = user.UserName,
            Module = SecurityModule,
            Action = "Login",
            EntityName = "User",
            EntityId = user.UserId,
            RecordRef = user.UserCode,
            Description = $"{user.UserName} logged in successfully.",
            IpAddress = ipAddress,
        }, cancellationToken);

        return new AuthResponseDto
        {
            Token = token,
            ExpiresAtUtc = expiresAtUtc,
            RefreshToken = refreshToken,
            RefreshTokenExpiresAtUtc = refreshExpiresAtUtc,
            User = UserService.MapToDto(user),
        };
    }

    public async Task<AuthResponseDto> RefreshAsync(RefreshTokenRequest request, string? ipAddress, CancellationToken cancellationToken)
    {
        var presented = request.RefreshToken?.Trim();
        if (string.IsNullOrEmpty(presented))
        {
            throw new UnauthorizedAccessException(SessionExpiredMessage);
        }

        var now = _dateTimeProvider.UtcNow;
        var presentedHash = _refreshTokenGenerator.Hash(presented);
        var stored = await _refreshTokenRepository.GetByHashAsync(presentedHash, cancellationToken);
        if (stored is null || stored.RevokedAt is not null || stored.ExpiresAt <= now)
        {
            throw new UnauthorizedAccessException(SessionExpiredMessage);
        }

        // BR-31: an inactive (or removed) user cannot keep a session going any more than they can log in.
        var user = await _userRepository.GetByIdAsync(stored.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            await _refreshTokenRepository.RevokeAsync(presentedHash, stored.UserId, now, ipAddress, cancellationToken);
            throw new UnauthorizedAccessException(SessionExpiredMessage);
        }

        var (refreshToken, refreshTokenHash, refreshExpiresAtUtc) = _refreshTokenGenerator.Create(now);
        var rotated = await _refreshTokenRepository.RotateAsync(presentedHash, new UserRefreshToken
        {
            UserId = user.UserId, TokenHash = refreshTokenHash, ExpiresAt = refreshExpiresAtUtc, CreatedAt = now, CreatedByIp = ipAddress,
        }, now, ipAddress, cancellationToken);
        if (!rotated)
        {
            throw new UnauthorizedAccessException(SessionExpiredMessage); // used by a concurrent request a moment ago
        }

        var (token, expiresAtUtc) = _jwtTokenService.GenerateAccessToken(user);
        return new AuthResponseDto
        {
            Token = token,
            ExpiresAtUtc = expiresAtUtc,
            RefreshToken = refreshToken,
            RefreshTokenExpiresAtUtc = refreshExpiresAtUtc,
            User = UserService.MapToDto(user),
        };
    }

    public async Task LogoutAsync(RefreshTokenRequest request, int userId, string? ipAddress, CancellationToken cancellationToken)
    {
        var presented = request.RefreshToken?.Trim();
        if (!string.IsNullOrEmpty(presented))
        {
            await _refreshTokenRepository.RevokeAsync(_refreshTokenGenerator.Hash(presented), userId, _dateTimeProvider.UtcNow, ipAddress, cancellationToken);
        }

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken);
        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = userId,
            UserName = user?.UserName ?? "Unknown",
            Module = SecurityModule,
            Action = "Logout",
            EntityName = "User",
            EntityId = userId,
            RecordRef = user?.UserCode,
            Description = $"{user?.UserName ?? "User " + userId} logged out.",
            IpAddress = ipAddress,
        }, cancellationToken);
    }
}
