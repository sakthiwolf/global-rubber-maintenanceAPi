using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace GlobalRubber.MMM.Application.Services;

public sealed class RoleService : IRoleService
{
    private const string SecurityModule = "Security"; // same audit "Module" value AuthService uses
    private const int RoleCodeMaxLength = 30;         // security.role_master.role_code VARCHAR(30)
    private const int RoleNameMaxLength = 100;        // role_name NVARCHAR(100)
    private const int DescriptionMaxLength = 200;     // description NVARCHAR(200)

    private static readonly Regex RoleCodePattern = new("^[A-Z0-9_]+$", RegexOptions.Compiled);

    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<RoleService> _logger;

    public RoleService(
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<RoleService> logger)
    {
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<PagedResult<RoleDto>> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken)
    {
        var (items, totalCount) = await _roleRepository.GetAllAsync(request, cancellationToken);

        return PagedResult<RoleDto>.Create(
            items.Select(MapToDto).ToList(),
            request.PageNumber,
            request.PageSize,
            totalCount);
    }

    public async Task<RoleDto> GetByIdAsync(int roleId, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Role), roleId);

        return MapToDto(role);
    }

    public async Task<RoleDto> CreateAsync(
        CreateRoleRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var (roleCode, roleName, description) = NormalizeAndValidate(request.RoleCode, request.RoleName, request.Description);

        if (await _roleRepository.ExistsByCodeAsync(roleCode, excludeRoleId: null, cancellationToken))
        {
            throw new ConflictException($"A role with code '{roleCode}' already exists.");
        }

        if (await _roleRepository.ExistsByNameAsync(roleName, excludeRoleId: null, cancellationToken))
        {
            throw new ConflictException($"A role named '{roleName}' already exists.");
        }

        var role = new Role
        {
            RoleCode = roleCode,
            RoleName = roleName,
            Description = description,
            IsSystemRole = false, // never client-controlled - see CreateRoleRequest
            IsActive = request.IsActive,
            CreatedAt = _dateTimeProvider.UtcNow,
            CreatedBy = actingUserId,
        };

        var created = await _roleRepository.AddAsync(role, cancellationToken);

        await WriteAuditAsync(
            "RoleCreated", created, actingUserId, ipAddress,
            userName => $"{userName} created role '{created.RoleName}' ({created.RoleCode}).", cancellationToken);

        return MapToDto(created);
    }

    public async Task<RoleDto> UpdateAsync(
        int roleId, UpdateRoleRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        // Loaded by GetByIdAsync (no tracking), so the role carries the row_version it was read
        // with - the repository's UpdateAsync uses it as the optimistic-concurrency check.
        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Role), roleId);

        var (roleCode, roleName, description) = NormalizeAndValidate(request.RoleCode, request.RoleName, request.Description);

        var codeChanged = !string.Equals(roleCode, role.RoleCode, StringComparison.OrdinalIgnoreCase);

        // Decided from the stored IsSystemRole - never from anything the client sent.
        if (role.IsSystemRole && codeChanged)
        {
            throw new ConflictException("The role code of a system role cannot be changed.");
        }

        if (codeChanged && await _roleRepository.ExistsByCodeAsync(roleCode, roleId, cancellationToken))
        {
            throw new ConflictException($"A role with code '{roleCode}' already exists.");
        }

        if (await _roleRepository.ExistsByNameAsync(roleName, roleId, cancellationToken))
        {
            throw new ConflictException($"A role named '{roleName}' already exists.");
        }

        var oldRoleCode = role.RoleCode;
        var changes = new List<string>();
        if (codeChanged)
        {
            changes.Add($"RoleCode '{oldRoleCode}' -> '{roleCode}'");
        }

        if (!string.Equals(role.RoleName, roleName, StringComparison.Ordinal))
        {
            changes.Add("RoleName");
        }

        if (!string.Equals(role.Description, description, StringComparison.Ordinal))
        {
            changes.Add("Description");
        }

        if (codeChanged)
        {
            role.RoleCode = roleCode;
        }

        role.RoleName = roleName;
        role.Description = description;
        role.UpdatedAt = _dateTimeProvider.UtcNow;
        role.UpdatedBy = actingUserId;

        var updated = await _roleRepository.UpdateAsync(role, cancellationToken);

        var changeSummary = changes.Count > 0 ? $"Changed: {string.Join(", ", changes)}." : "No field values changed.";

        await WriteAuditAsync(
            "RoleUpdated", updated, actingUserId, ipAddress,
            userName => $"{userName} updated role '{updated.RoleName}' ({updated.RoleCode}). {changeSummary}",
            cancellationToken);

        return MapToDto(updated);
    }

    public async Task<RoleDto> DeactivateAsync(
        int roleId, int? actingUserId, string? ipAddress, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Role), roleId);

        // Decided from the stored IsSystemRole - never from a role name or anything the client sent.
        if (role.IsSystemRole)
        {
            throw new ConflictException("A system role cannot be deactivated.");
        }

        // Explicit rather than a silent no-op: nothing is written and nothing is audited.
        if (!role.IsActive)
        {
            throw new ConflictException("The role is already inactive.");
        }

        // Existence only - no user details are loaded or ever put in the message.
        if (await _userRepository.AnyActiveByRoleIdAsync(roleId, cancellationToken))
        {
            throw new ConflictException("The role cannot be deactivated while active users are assigned to it.");
        }

        role.IsActive = false;
        role.UpdatedAt = _dateTimeProvider.UtcNow;
        role.UpdatedBy = actingUserId;

        var deactivated = await _roleRepository.DeactivateAsync(role, cancellationToken);

        await WriteAuditAsync(
            "RoleDeactivated", deactivated, actingUserId, ipAddress,
            userName => $"{userName} deactivated role '{deactivated.RoleName}' ({deactivated.RoleCode}).",
            cancellationToken);

        return MapToDto(deactivated);
    }

    // Trim everything; the code is also upper-cased and a blank description becomes null. One
    // place so Create and Update can never disagree about what a valid role looks like.
    private static (string RoleCode, string RoleName, string? Description) NormalizeAndValidate(
        string? rawRoleCode, string? rawRoleName, string? rawDescription)
    {
        var roleCode = (rawRoleCode ?? string.Empty).Trim().ToUpperInvariant();
        var roleName = (rawRoleName ?? string.Empty).Trim();
        var description = string.IsNullOrWhiteSpace(rawDescription) ? null : rawDescription.Trim();

        var errors = new List<string>();

        if (roleCode.Length == 0)
        {
            errors.Add("RoleCode is required.");
        }
        else if (roleCode.Length > RoleCodeMaxLength)
        {
            errors.Add($"RoleCode must be at most {RoleCodeMaxLength} characters.");
        }
        else if (!RoleCodePattern.IsMatch(roleCode))
        {
            errors.Add("RoleCode may contain only letters A-Z, digits 0-9 and underscore.");
        }

        if (roleName.Length == 0)
        {
            errors.Add("RoleName is required.");
        }
        else if (roleName.Length > RoleNameMaxLength)
        {
            errors.Add($"RoleName must be at most {RoleNameMaxLength} characters.");
        }

        if (description is not null && description.Length > DescriptionMaxLength)
        {
            errors.Add($"Description must be at most {DescriptionMaxLength} characters.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationException(errors);
        }

        return (roleCode, roleName, description);
    }

    // Everything after the business write has succeeded is audit bookkeeping and must never turn
    // that success into a failure - AuditLogService already swallows its own persistence errors,
    // and the user-name lookup below is guarded the same way.
    private async Task WriteAuditAsync(
        string action, Role role, int? actingUserId, string? ipAddress,
        Func<string, string> describe, CancellationToken cancellationToken)
    {
        var actingUserName = await ResolveUserNameAsync(actingUserId, cancellationToken);

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = SecurityModule,
            Action = action,
            EntityName = "Role",
            EntityId = role.RoleId,
            RecordRef = role.RoleCode,
            Description = describe(actingUserName),
            IpAddress = ipAddress,
        }, cancellationToken);
    }

    // audit_log.user_name is a NOT NULL snapshot column and the JWT carries no user name, so it
    // is looked up here; a missing/unresolvable user never blocks the (already completed) write.
    private Task<string> ResolveUserNameAsync(int? userId, CancellationToken cancellationToken) =>
        AuditUserNameResolver.ResolveAsync(_userRepository, _logger, userId, cancellationToken);

    private static RoleDto MapToDto(Role role) => new()
    {
        RoleId = role.RoleId,
        RoleCode = role.RoleCode,
        RoleName = role.RoleName,
        Description = role.Description,
        IsSystemRole = role.IsSystemRole,
        IsActive = role.IsActive,
    };
}
