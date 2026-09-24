using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

public sealed class UserService : IUserService
{
    private const string SecurityModule = "Security"; // same audit "Module" value AuthService/RoleService use
    private const int LoginIdMaxLength = 50;          // security.user_master.login_id VARCHAR(50)
    private const int UserNameMaxLength = 100;        // user_name NVARCHAR(100)
    private const int EmailMaxLength = 150;           // email VARCHAR(150)
    private const int MobileMaxLength = 15;           // mobile VARCHAR(15)

    private readonly IUserRepository _userRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserRepository userRepository,
        IRoleRepository roleRepository,
        IPasswordHasher passwordHasher,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<UserService> logger)
    {
        _userRepository = userRepository;
        _roleRepository = roleRepository;
        _passwordHasher = passwordHasher;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<UserDto>> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _userRepository.GetAllAsync(request, cancellationToken);

        return PagedResult<UserDto>.Create(
            items.Select(MapToDto).ToList(),
            request.PageNumber,
            request.PageSize,
            totalCount);
    }

    public async Task<UserDto> GetByIdAsync(int userId, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        return MapToDto(user);
    }

    public async Task<UserDto> CreateAsync(
        CreateUserRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var profile = NormalizeProfile(request.LoginId, request.UserName, request.Email, request.Mobile);
        var errors = ValidateProfile(profile);

        // The only password rule that exists in the project today is AuthService's "not empty" - the real
        // policy (length/complexity) is still an open question (Q-37), so none is invented here. The
        // password is deliberately not trimmed.
        var password = request.Password ?? string.Empty;
        if (password.Length == 0)
        {
            errors.Add("Password is required.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var role = await LoadAssignableRoleAsync(request.RoleId, cancellationToken);

        if (await _userRepository.ExistsByLoginIdAsync(profile.LoginId, excludeUserId: null, cancellationToken))
        {
            throw new ConflictException($"A user with login ID '{profile.LoginId}' already exists.");
        }

        var user = new User
        {
            // UserId / UserCode are never client-controlled: the code is issued by the repository from the
            // USER document sequence inside the insert's transaction.
            LoginId = profile.LoginId,
            UserName = profile.UserName,
            Email = profile.Email,
            Mobile = profile.Mobile,
            RoleId = role.RoleId,
            PasswordHash = _passwordHasher.HashPassword(password),
            MustChangePassword = true, // admin-set password is temporary
            IsActive = request.IsActive,
            CreatedAt = _dateTimeProvider.UtcNow,
            CreatedBy = actingUserId,
        };

        var created = await _userRepository.AddAsync(user, cancellationToken);
        created.Role = role;

        await WriteAuditAsync(new AuditLogEntry
        {
            Action = "UserCreated",
            EntityId = created.UserId,
            RecordRef = created.UserCode,
            Description = $"{{actor}} created user '{created.UserName}' ({created.UserCode}) with role {role.RoleCode}.",
        }, actingUserId, ipAddress, cancellationToken);

        return MapToDto(created);
    }

    public async Task<UserDto> UpdateAsync(
        int userId, UpdateUserRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        // Loaded without tracking; the caller's row version (not the one just read) is what guards the save.
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException(nameof(User), userId);

        var profile = NormalizeProfile(request.LoginId, request.UserName, request.Email, request.Mobile);
        var errors = ValidateProfile(profile);

        byte[]? originalRowVersion = null;
        if (string.IsNullOrWhiteSpace(request.RowVersion))
        {
            errors.Add("RowVersion is required.");
        }
        else if (!TryDecodeRowVersion(request.RowVersion, out originalRowVersion))
        {
            errors.Add("RowVersion is not valid.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        var roleChanged = request.RoleId != user.RoleId;
        var oldRoleCode = user.Role.RoleCode;
        var role = user.Role;
        if (roleChanged)
        {
            role = await LoadAssignableRoleAsync(request.RoleId, cancellationToken);
        }

        // The uniqueness check is case-insensitive (login_id's collation), but a re-cased login ID is still a
        // change to save - it just cannot collide with another user unless the letters differ.
        var loginIdChanged = !string.Equals(profile.LoginId, user.LoginId, StringComparison.Ordinal);
        var loginIdDiffersIgnoringCase = !string.Equals(profile.LoginId, user.LoginId, StringComparison.OrdinalIgnoreCase);
        if (loginIdDiffersIgnoringCase && await _userRepository.ExistsByLoginIdAsync(profile.LoginId, userId, cancellationToken))
        {
            throw new ConflictException($"A user with login ID '{profile.LoginId}' already exists.");
        }

        // Field names only - never values (email/mobile/password are not audit material).
        var changes = new List<string>();
        if (loginIdChanged) changes.Add("LoginId");
        if (!string.Equals(user.UserName, profile.UserName, StringComparison.Ordinal)) changes.Add("UserName");
        if (!string.Equals(user.Email, profile.Email, StringComparison.Ordinal)) changes.Add("Email");
        if (!string.Equals(user.Mobile, profile.Mobile, StringComparison.Ordinal)) changes.Add("Mobile");
        if (roleChanged) changes.Add("Role");
        if (user.IsActive != request.IsActive) changes.Add("Status");

        var passwordChanged = !string.IsNullOrEmpty(request.NewPassword);
        if (passwordChanged) changes.Add("Password");

        if (loginIdChanged) user.LoginId = profile.LoginId;
        user.UserName = profile.UserName;
        user.Email = profile.Email;
        user.Mobile = profile.Mobile;
        user.RoleId = role.RoleId;
        user.IsActive = request.IsActive;
        if (passwordChanged)
        {
            user.PasswordHash = _passwordHasher.HashPassword(request.NewPassword!);
            user.MustChangePassword = true; // admin-set password is temporary
        }

        user.UpdatedAt = _dateTimeProvider.UtcNow;
        user.UpdatedBy = actingUserId;

        var updated = await _userRepository.UpdateAsync(user, originalRowVersion!, passwordChanged, cancellationToken);
        updated.Role = role;

        var changeSummary = changes.Count > 0 ? $"Changed: {string.Join(", ", changes)}." : "No field values changed.";

        await WriteAuditAsync(new AuditLogEntry
        {
            Action = "UserUpdated",
            EntityId = updated.UserId,
            RecordRef = updated.UserCode,
            Description = $"{{actor}} updated user '{updated.UserName}' ({updated.UserCode}). {changeSummary}",
        }, actingUserId, ipAddress, cancellationToken);

        if (roleChanged)
        {
            await WriteAuditAsync(new AuditLogEntry
            {
                Action = "UserRoleChanged",
                EntityId = updated.UserId,
                RecordRef = updated.UserCode,
                Description = $"{{actor}} changed the role of user '{updated.UserName}' ({updated.UserCode}). Role: {oldRoleCode} -> {role.RoleCode}.",
                Details = new[] { new AuditLogDetailEntry("role_code", oldRoleCode, role.RoleCode) },
            }, actingUserId, ipAddress, cancellationToken);
        }

        return MapToDto(updated);
    }

    // The selected role must exist (404) and be active (400) - an inactive role cannot be assigned.
    private async Task<Role> LoadAssignableRoleAsync(int roleId, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Role), roleId);

        if (!role.IsActive)
        {
            throw new ValidationException("The selected role is not active.");
        }

        return role;
    }

    private static (string LoginId, string UserName, string? Email, string? Mobile) NormalizeProfile(
        string? loginId, string? userName, string? email, string? mobile) => (
            (loginId ?? string.Empty).Trim(),
            (userName ?? string.Empty).Trim(),
            string.IsNullOrWhiteSpace(email) ? null : email.Trim(),
            string.IsNullOrWhiteSpace(mobile) ? null : mobile.Trim());

    private static List<string> ValidateProfile((string LoginId, string UserName, string? Email, string? Mobile) p)
    {
        var errors = new List<string>();

        if (p.LoginId.Length == 0) errors.Add("LoginId is required.");
        else if (p.LoginId.Length > LoginIdMaxLength) errors.Add($"LoginId must be at most {LoginIdMaxLength} characters.");

        if (p.UserName.Length == 0) errors.Add("UserName is required.");
        else if (p.UserName.Length > UserNameMaxLength) errors.Add($"UserName must be at most {UserNameMaxLength} characters.");

        if (p.Email is not null && p.Email.Length > EmailMaxLength) errors.Add($"Email must be at most {EmailMaxLength} characters.");
        if (p.Mobile is not null && p.Mobile.Length > MobileMaxLength) errors.Add($"Mobile must be at most {MobileMaxLength} characters.");

        return errors;
    }

    private static bool TryDecodeRowVersion(string value, out byte[]? bytes)
    {
        var buffer = new byte[value.Length];
        if (Convert.TryFromBase64String(value, buffer, out var written) && written > 0)
        {
            bytes = buffer[..written];
            return true;
        }

        bytes = null;
        return false;
    }

    // Everything after the business write has succeeded is audit bookkeeping and never fails it.
    private async Task WriteAuditAsync(
        AuditLogEntry template, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var actingUserName = await AuditUserNameResolver.ResolveAsync(_userRepository, _logger, actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = SecurityModule,
            Action = template.Action,
            EntityName = "User",
            EntityId = template.EntityId,
            RecordRef = template.RecordRef,
            Description = template.Description.Replace("{actor}", actingUserName),
            IpAddress = ipAddress,
            Details = template.Details,
        }, cancellationToken);
    }

    /// <summary>
    /// Internal (not private) so AuthService can reuse the exact same mapping when building a
    /// login response, instead of duplicating it or issuing a second GetByIdAsync round-trip
    /// right after authentication already loaded the user.
    /// </summary>
    internal static UserDto MapToDto(User user) => new()
    {
        UserId = user.UserId,
        UserCode = user.UserCode,
        LoginId = user.LoginId,
        UserName = user.UserName,
        RoleId = user.RoleId,
        RoleName = user.Role.RoleName,
        EmployeeId = user.EmployeeId,
        DepartmentId = user.DepartmentId,
        Email = user.Email,
        Mobile = user.Mobile,
        MustChangePassword = user.MustChangePassword,
        LastLoginAt = user.LastLoginAt,
        IsActive = user.IsActive,
        RowVersion = Convert.ToBase64String(user.RowVersion),
    };
}
