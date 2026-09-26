using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IMachinePmRepository
{
    /// <summary>
    /// A page of PMs (machine, checklist and snapshot lines included), optionally one tab (MachinePmBucket: DUE open PMs of a
    /// checklist frequency - scheduled on or before <paramref name="today"/>, the plant's IST date - or completed). Open tabs
    /// are ordered by scheduled date, Completed by completion date (newest first).
    /// </summary>
    Task<(IReadOnlyList<MachinePm> Items, int TotalCount)> GetAllAsync(MachinePmListQuery request, DateOnly today, CancellationToken cancellationToken);

    /// <summary>How many PMs fall in each tab on <paramref name="today"/>, under the query's machine/search filters (the tab itself is ignored).</summary>
    Task<MachinePmBucketCountsDto> GetBucketCountsAsync(MachinePmListQuery request, DateOnly today, CancellationToken cancellationToken);

    /// <summary>One PM with its machine, checklist and snapshot lines, untracked; null if it does not exist.</summary>
    Task<MachinePm?> GetByIdAsync(int machinePmId, CancellationToken cancellationToken);

    Task<MachinePmLookupsDto> GetLookupsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Completes a PM as ONE unit (Function 5): the machine rows are locked (ascending id); the PM header (status, completed
    /// date, maintenance by, remarks, updated columns - guarded by <paramref name="originalRowVersion"/>) and its lines'
    /// is_checked are written; the plan's successor (if any) is inserted with a MACHINE_PM number and a snapshot of the
    /// checklist's CURRENT items; every locked machine's dates are set by the plan. Anything that throws rolls back all of it.
    /// </summary>
    /// <exception cref="GlobalRubber.MMM.Application.Common.ConflictException">The PM's row version no longer matches, or a successor already exists.</exception>
    Task<MachinePm> CompleteAsync(MachinePm pm, byte[] originalRowVersion, MachinePmCompletionPlan plan, CancellationToken cancellationToken);
}
