using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Services;

public sealed class RoleService : IRoleService
{
    private readonly IRoleRepository _roleRepository;

    public RoleService(IRoleRepository roleRepository)
    {
        _roleRepository = roleRepository;
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
