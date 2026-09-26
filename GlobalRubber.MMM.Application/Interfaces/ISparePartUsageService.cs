using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface ISparePartUsageService
{
    Task<PagedResult<SparePartUsageDto>> GetAllAsync(SparePartUsageListQuery query, CancellationToken cancellationToken);

    Task<SparePartUsageDto> GetByIdAsync(int sparePartUsageId, CancellationToken cancellationToken);

    Task<SparePartUsageLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);

    /// <summary>Posts a usage; <c>Created</c> is false when the request id had already been posted (the existing usage).</summary>
    Task<(SparePartUsageDto Usage, bool Created)> CreateAsync(
        CreateSparePartUsageRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    Task<SparePartUsageDto> ReverseAsync(
        int sparePartUsageId, ReverseSparePartUsageRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
