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
    /// The document number is assigned by this method. When <paramref name="machinePlan"/> is given, the machine row is
    /// locked first and the plan's machine change is saved in the same transaction (BR-16).
    /// </summary>
    /// <exception cref="ConflictException">If the generated breakdown number already exists (sequence/table mismatch).</exception>
    Task<MachineBreakdown> AddAsync(MachineBreakdown breakdown, BreakdownMachineStatusPlan? machinePlan, CancellationToken cancellationToken);

    /// <summary>
    /// Updates the breakdown's stage and related fields in a single transaction.
    /// Uses optimistic concurrency (row_version). When <paramref name="machinePlan"/> is given, the machine row is locked
    /// first and the plan's machine change is saved in the same transaction (BR-20).
    /// </summary>
    /// <exception cref="ConflictException">If the row was modified concurrently.</exception>
    Task<MachineBreakdown> UpdateStageAsync(
        MachineBreakdown breakdown, byte[] originalRowVersion, BreakdownMachineStatusPlan? machinePlan, CancellationToken cancellationToken);
}
