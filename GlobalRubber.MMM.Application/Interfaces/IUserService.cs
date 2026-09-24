using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IUserService
{
    Task<PagedResult<UserDto>> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No user exists with the given id.</exception>
    Task<UserDto> GetByIdAsync(int userId, CancellationToken cancellationToken);
}
