using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Services;

public sealed class UserService : IUserService
{
    private readonly IUserRepository _userRepository;

    public UserService(IUserRepository userRepository)
    {
        _userRepository = userRepository;
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
    };
}
