using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IBreakdownTypeRepository
{
    Task<(IReadOnlyList<BreakdownType> Items, int TotalCount)> GetAllAsync(
        BreakdownTypeListQuery request, CancellationToken cancellationToken);

    Task<BreakdownType?> GetByIdAsync(
        int breakdownTypeId,
        CancellationToken cancellationToken);

    Task<bool> ExistsActiveByNameAsync(
        string name, int? excludeBreakdownTypeId, CancellationToken cancellationToken);

    Task<BreakdownType> AddAsync(
        BreakdownType breakdownType,
        CancellationToken cancellationToken);

    Task<BreakdownType> UpdateAsync(
        BreakdownType breakdownType,
        byte[] originalRowVersion,
        CancellationToken cancellationToken);

    Task<BreakdownType> DeactivateAsync(
        BreakdownType breakdownType,
        CancellationToken cancellationToken);
}