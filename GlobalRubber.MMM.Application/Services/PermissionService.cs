using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Services;

public sealed class PermissionService : IPermissionService
{
    private readonly IPermissionRepository _permissionRepository;
    private readonly IRoleRepository _roleRepository;
    private readonly IDateTimeProvider _dateTimeProvider;

    public PermissionService(
        IPermissionRepository permissionRepository,
        IRoleRepository roleRepository,
        IDateTimeProvider dateTimeProvider)
    {
        _permissionRepository = permissionRepository;
        _roleRepository = roleRepository;
        _dateTimeProvider = dateTimeProvider;
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
        int roleId, UpdateRolePermissionsRequest request, CancellationToken cancellationToken)
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
            UpdatedAt = now,
        }).ToList();

        await _permissionRepository.SaveRolePermissionsAsync(roleId, permissionsToSave, cancellationToken);

        var savedPermissions = await _permissionRepository.GetByRoleIdAsync(roleId, cancellationToken);

        return BuildMatrix(role, activeModules, savedPermissions);
    }

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
