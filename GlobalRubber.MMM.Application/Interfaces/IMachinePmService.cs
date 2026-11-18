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
    /// The year's PM calendar (F05 layout): real jobs with their status plus projected "Planned" occurrences. Year defaults
    /// to the current plant year; it must be between 2000 and 2100.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">The year is out of range.</exception>
    Task<MachinePmCalendarDto> GetCalendarAsync(int? year, CancellationToken cancellationToken);

    /// <summary>
    /// Completes a PM and, when its checklist is still an active Machine checklist, creates exactly one successor - all in
    /// one transaction. Writes MachinePmCompleted (and MachinePmScheduled for the successor).
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.NotFoundException">No PM exists with the given id.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ValidationException">Maintenance By, remarks, a checklist result or the row version is invalid.</exception>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">It is already completed, or it was modified after it was read.</exception>
    /// <summary>Scheduled -> In Progress; the machine goes under Maintenance (A1). 409 unless the PM is Scheduled.</summary>
    Task<MachinePmDto> StartAsync(int machinePmId, StartMachinePmRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    Task<MachinePmDto> CompleteAsync(int machinePmId, CompleteMachinePmRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
