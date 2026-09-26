using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IMachineBreakdownRepository
{
    /// <summary>
    /// A page of breakdowns (machine and breakdown type included), ordered newest first.
    /// Supports search (BreakdownNo / MachineCode / MachineName / Problem / ReportedBy) and
    /// stage filter.
    /// </summary>
    Task<(IReadOnlyList<MachineBreakdown> Items, int TotalCount)> GetAllAsync(
        MachineBreakdownListQuery query, CancellationToken cancellationToken);

    /// <summary>One breakdown with machine, breakdown type and assigned engineer; null if not found.</summary>
    Task<MachineBreakdown?> GetByIdAsync(int machineBreakdownId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the breakdown and increments the MACHINE_BREAKDOWN sequence in a single transaction.
    /// The document number is assigned by this method.
    /// </summary>
    /// <exception cref="ConflictException">If the generated breakdown number already exists (sequence/table mismatch).</exception>
    Task<MachineBreakdown> AddAsync(MachineBreakdown breakdown, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the breakdown's stage and related fields in a single transaction.
    /// Uses optimistic concurrency (row_version).
    /// </summary>
    /// <exception cref="ConflictException">If the row was modified concurrently.</exception>
    Task<MachineBreakdown> UpdateStageAsync(
        MachineBreakdown breakdown, byte[] originalRowVersion, CancellationToken cancellationToken);
}
