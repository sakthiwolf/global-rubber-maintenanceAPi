using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IBreakdownTypeService
{
    Task<PagedResult<BreakdownTypeDto>> GetAllAsync(
        BreakdownTypeListQuery query,
        CancellationToken cancellationToken);

    Task<BreakdownTypeDto> GetByIdAsync(
        int breakdownTypeId,
        CancellationToken cancellationToken);

    Task<BreakdownTypeDto> CreateAsync(
        CreateBreakdownTypeRequest request,
        int? actingUserId,
        string? ipAddress,
        CancellationToken cancellationToken);

    Task<BreakdownTypeDto> UpdateAsync(
        int breakdownTypeId,
        UpdateBreakdownTypeRequest request,
        int? actingUserId,
        string? ipAddress,
        CancellationToken cancellationToken);

    Task<BreakdownTypeDto> DeactivateAsync(
        int breakdownTypeId,
        int? actingUserId,
        string? ipAddress,
        CancellationToken cancellationToken);
}