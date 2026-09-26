using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface IMoldPmService
{
    Task<PagedResult<MoldPmDto>> GetAllAsync(MoldPmListQuery query, CancellationToken cancellationToken);

    Task<MoldPmCountsDto> GetCountsAsync(MoldPmListQuery query, CancellationToken cancellationToken);

    Task<MoldPmDto> GetByIdAsync(int moldPmId, CancellationToken cancellationToken);

    Task<IReadOnlyList<MoldUsageDto>> GetMoldUsageAsync(CancellationToken cancellationToken);

    Task<MoldPmDto> StartAsync(int moldPmId, StartMoldPmRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    Task<MoldPmDto> CompleteAsync(int moldPmId, CompleteMoldPmRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
