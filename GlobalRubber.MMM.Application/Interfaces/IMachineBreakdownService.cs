using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// Machine Breakdown - report, track and resolve machine breakdown incidents.
/// Stage flows forward only: Reported → Assigned → Maintenance Started → Resolved → Closed.
/// </summary>
public interface IMachineBreakdownService
{
    /// <summary>
    /// A page of breakdowns, ordered newest first. Supports search and stage filter.
    /// </summary>
    Task<PagedResult<MachineBreakdownDto>> GetAllAsync(
        MachineBreakdownListQuery query, CancellationToken cancellationToken);

    /// <summary>One breakdown by id.</summary>
    /// <exception cref="NotFoundException">No breakdown exists with the given id.</exception>
    Task<MachineBreakdownDto> GetByIdAsync(int machineBreakdownId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new breakdown with stage = Reported and a generated BRK-NNNN number.
    /// Validates the machine (exists, active). ReportedBy is stored as free text.
    /// </summary>
    /// <exception cref="ValidationException">Required fields missing, invalid priority, or machine is inactive.</exception>
    /// <exception cref="NotFoundException">Machine or BreakdownType not found.</exception>
    /// <exception cref="ConflictException">Duplicate breakdown number (sequence/table mismatch - retry).</exception>
    Task<MachineBreakdownDto> CreateAsync(
        CreateMachineBreakdownRequest request, int? actingUserId, string? ipAddress, CancellationToken cancellationToken);

    /// <summary>
    /// Advances the breakdown stage by one step. Uses optimistic concurrency (rowVersion).
    /// </summary>
    /// <exception cref="NotFoundException">No breakdown exists with the given id.</exception>
    /// <exception cref="ValidationException">Invalid stage transition or missing required fields.</exception>
    /// <exception cref="ConflictException">Row was modified concurrently; refresh and try again.</exception>
    Task<MachineBreakdownDto> AdvanceStageAsync(
        int machineBreakdownId, AdvanceMachineBreakdownStageRequest request,
        int? actingUserId, string? ipAddress, CancellationToken cancellationToken);
}
