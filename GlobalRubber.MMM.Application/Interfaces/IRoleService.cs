using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IRoleService
{
    Task<PagedResult<RoleDto>> GetAllAsync(PaginationRequest request, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No role exists with the given id.</exception>
    Task<RoleDto> GetByIdAsync(int roleId, CancellationToken cancellationToken);
}
