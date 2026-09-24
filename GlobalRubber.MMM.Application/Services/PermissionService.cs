using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

public sealed class PermissionService : IPermissionService
{
    private const string SecurityModule = "Security"; // same audit "Module" value AuthService/RoleService use

    private readonly IPermissionRepository _permissionRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IUserRepository _userRepository;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<PermissionService> _logger;

    public PermissionService(
        IPermissionRepository permissionRepository,
        IRoleRepository roleRepository,
        IUserRepository userRepository,
        IDateTimeProvider dateTimeProvider,
        IAuditLogService auditLogService,
        ILogger<PermissionService> logger)
    {
        _permissionRepository = permissionRepository;
        _roleRepository = roleRepository;
        _userRepository = userRepository;
        _dateTimeProvider = dateTimeProvider;
        _auditLogService = auditLogService;
        _logger = logger;
    }

    public async Task<RolePermissionMatrixDto> GetMyPermissionsAsync(int userId, CancellationToken cancellationToken)
    {
        // The role comes from the user's own record, so a caller can only ever read their own matrix.
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new UnauthorizedAccessException();

        return await GetRolePermissionsAsync(user.RoleId, cancellationToken);
    }

    public async Task<RolePermissionMatrixDto> GetRolePermissionsAsync(int roleId, CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Role), roleId);

        var modules = await _permissionRepository.GetActiveModulesAsync(cancellationToken);
        var permissions = await _permissionRepository.GetByRoleIdAsync(roleId, cancellationToken);

        return BuildMatrix(role, modules, permissions);
    }

    public async Task<RolePermissionMatrixDto> UpdateRolePermissionsAsync(
        int roleId, UpdateRolePermissionsRequest request, int? actingUserId, string? ipAddress,
        CancellationToken cancellationToken)
    {
        var role = await _roleRepository.GetByIdAsync(roleId, cancellationToken)
            ?? throw new NotFoundException(nameof(Role), roleId);

        var updates = request.Permissions ?? Array.Empty<ModulePermissionUpdateDto>();

        var duplicateModuleIds = updates
            .GroupBy(u => u.ModuleId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateModuleIds.Count > 0)
        {
            throw new ValidationException(
                $"Duplicate moduleId(s) in request: {string.Join(", ", duplicateModuleIds)}.");
        }

        var activeModules = await _permissionRepository.GetActiveModulesAsync(cancellationToken);
        var activeModuleIds = activeModules.Select(m => m.ModuleId).ToHashSet();

        var unknownModuleIds = updates
            .Select(u => u.ModuleId)
            .Where(id => !activeModuleIds.Contains(id))
            .Distinct()
            .ToList();

        if (unknownModuleIds.Count > 0)
        {
            throw new ValidationException(
                $"Unknown or inactive moduleId(s): {string.Join(", ", unknownModuleIds)}.");
        }

        // Old values, captured BEFORE the save: a missing row means "no access", i.e. all false.
        var existing = (await _permissionRepository.GetByRoleIdAsync(roleId, cancellationToken))
            .ToDictionary(p => p.ModuleId, p => Flags(p));
        var moduleCodes = activeModules.ToDictionary(m => m.ModuleId, m => m.ModuleCode);
        var auditDetails = BuildAuditDetails(updates, existing, moduleCodes);

        var now = _dateTimeProvider.UtcNow;

        var permissionsToSave = updates.Select(u => new Permission
        {
            RoleId = roleId,
            ModuleId = u.ModuleId,
            CanView = u.CanView,
            CanAdd = u.CanAdd,
            CanEdit = u.CanEdit,
            CanDelete = u.CanDelete,
            CanApprove = u.CanApprove,
            CanExport = u.CanExport,
            CreatedAt = now,
            CreatedBy = actingUserId,
            UpdatedAt = now,
            UpdatedBy = actingUserId,
        }).ToList();

        await _permissionRepository.SaveRolePermissionsAsync(roleId, permissionsToSave, cancellationToken);

        // Only after the save has succeeded. Audit problems never fail the (already committed) save:
        // AuditLogService swallows its own persistence errors, and the name lookup is guarded here.
        var actingUserName = await ResolveUserNameAsync(actingUserId, cancellationToken);
        var changedModules = auditDetails.Select(d => d.FieldName.Split('.')[0]).Distinct().Count();

        await _auditLogService.LogAsync(new AuditLogEntry
        {
            UserId = actingUserId,
            UserName = actingUserName,
            Module = SecurityModule,
            Action = "PermissionsUpdated",
            EntityName = "Role",
            EntityId = role.RoleId,
            RecordRef = role.RoleCode,
            Description = auditDetails.Count == 0
                ? $"{actingUserName} saved permissions for role '{role.RoleName}' ({role.RoleCode}); no permission values changed."
                : $"{actingUserName} updated permissions for role '{role.RoleName}' ({role.RoleCode}): {auditDetails.Count} change(s) in {changedModules} module(s).",
            IpAddress = ipAddress,
            Details = auditDetails,
        }, cancellationToken);

        var savedPermissions = await _permissionRepository.GetByRoleIdAsync(roleId, cancellationToken);

        return BuildMatrix(role, activeModules, savedPermissions);
    }

    private static readonly (string Column, Func<PermissionActionFlags, bool> Get)[] FlagColumns =
    {
        ("can_view", f => f.CanView),
        ("can_add", f => f.CanAdd),
        ("can_edit", f => f.CanEdit),
        ("can_delete", f => f.CanDelete),
        ("can_approve", f => f.CanApprove),
        ("can_export", f => f.CanExport),
    };

    private static PermissionActionFlags Flags(Permission p) =>
        new(p.CanView, p.CanAdd, p.CanEdit, p.CanDelete, p.CanApprove, p.CanExport);

    // One detail row per flag that actually changes: field_name "<MODULE_CODE>.<column>" (the table has no
    // separate module column), values "true"/"false". Unchanged flags produce nothing.
    private static List<AuditLogDetailEntry> BuildAuditDetails(
        IReadOnlyList<ModulePermissionUpdateDto> updates,
        Dictionary<int, PermissionActionFlags> existing,
        Dictionary<int, string> moduleCodes)
    {
        var details = new List<AuditLogDetailEntry>();

        foreach (var update in updates.OrderBy(u => moduleCodes[u.ModuleId], StringComparer.Ordinal))
        {
            var before = existing.TryGetValue(update.ModuleId, out var flags) ? flags : new PermissionActionFlags(false, false, false, false, false, false);
            var after = new PermissionActionFlags(
                update.CanView, update.CanAdd, update.CanEdit, update.CanDelete, update.CanApprove, update.CanExport);

            foreach (var (column, get) in FlagColumns)
            {
                var oldValue = get(before);
                var newValue = get(after);
                if (oldValue != newValue)
                {
                    details.Add(new AuditLogDetailEntry(
                        $"{moduleCodes[update.ModuleId]}.{column}",
                        oldValue ? "true" : "false",
                        newValue ? "true" : "false"));
                }
            }
        }

        return details;
    }

    private Task<string> ResolveUserNameAsync(int? userId, CancellationToken cancellationToken) =>
        AuditUserNameResolver.ResolveAsync(_userRepository, _logger, userId, cancellationToken);

    private static RolePermissionMatrixDto BuildMatrix(
        Role role, IReadOnlyList<Module> modules, IReadOnlyList<Permission> permissions)
    {
        var permissionsByModuleId = permissions.ToDictionary(p => p.ModuleId);

        var items = modules.Select(module =>
        {
            permissionsByModuleId.TryGetValue(module.ModuleId, out var permission);

            return new ModulePermissionDto
            {
                ModuleId = module.ModuleId,
                ModuleCode = module.ModuleCode,
                ModuleName = module.ModuleName,
                MenuGroup = module.MenuGroup,
                SortOrder = module.SortOrder,
                CanView = permission?.CanView ?? false,
                CanAdd = permission?.CanAdd ?? false,
                CanEdit = permission?.CanEdit ?? false,
                CanDelete = permission?.CanDelete ?? false,
                CanApprove = permission?.CanApprove ?? false,
                CanExport = permission?.CanExport ?? false,
            };
        }).ToList();

        return new RolePermissionMatrixDto
        {
            RoleId = role.RoleId,
            RoleCode = role.RoleCode,
            RoleName = role.RoleName,
            Permissions = items,
        };
    }
}
