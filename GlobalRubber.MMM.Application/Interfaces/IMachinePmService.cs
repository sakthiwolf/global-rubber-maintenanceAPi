using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// Machine Preventive Maintenance - recurring occurrences driven by the Maintenance Checklist. Occurrences are CREATED only
/// by the checklist (first occurrence) and by completion (successor); there is no manual scheduling, start step, edit,
/// cancel or delete.
/// </summary>
public interface IMachinePmService
{
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">The bucket is not a known value.</exception>
    Task<PagedResult<MachinePmDto>> GetAllAsync(MachinePmListQuery query, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">The bucket is not a known value.</exception>
    Task<MachinePmBucketCountsDto> GetBucketCountsAsync(MachinePmListQuery query, CancellationToken cancellationToken);

    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No PM exists with the given id.</exception>
    Task<MachinePmDto> GetByIdAsync(int machinePmId, CancellationToken cancellationToken);

    Task<MachinePmLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Completes a PM and, when its checklist is still an active Machine checklist, creates exactly one successor - all in
    /// one transaction. Writes MachinePmCompleted (and MachinePmScheduled for the successor).
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No PM exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">Maintenance By, remarks, a checklist result or the row version is invalid.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">It is already completed, or it was modified after it was read.</exception>
    Task<MachinePmDto> CompleteAsync(int machinePmId, CompleteMachinePmRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
