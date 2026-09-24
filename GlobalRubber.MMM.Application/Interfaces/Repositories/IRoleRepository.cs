using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IRoleRepository
{
    Task<(IReadOnlyList<Role> Items, int TotalCount)> GetAllAsync(
        PaginationRequest request, CancellationToken cancellationToken);

    Task<Role?> GetByIdAsync(int roleId, CancellationToken cancellationToken);
}
