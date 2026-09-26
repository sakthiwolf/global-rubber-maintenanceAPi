using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

public interface IMoldPmRepository
{
    /// <summary>
    /// A page of Mold PMs (mold included). The due / overdue tabs compare the PM's due date with <paramref name="today"/>
    /// (IST). Open work oldest-due first; completed work newest first.
    /// </summary>
    Task<(IReadOnlyList<MoldPm> Items, int TotalCount)> GetAllAsync(MoldPmListQuery request, DateOnly today, CancellationToken cancellationToken);

    Task<MoldPmCountsDto> GetCountsAsync(MoldPmListQuery request, DateOnly today, CancellationToken cancellationToken);

    /// <summary>One PM with its mold, untracked; null if it does not exist.</summary>
    Task<MoldPm?> GetByIdAsync(int moldPmId, CancellationToken cancellationToken);

    /// <summary>Every mold's PM position (ordered by code): usage, configuration, open PM and last completed automatic PM.</summary>
    Task<IReadOnlyList<MoldUsageSnapshot>> GetMoldUsageAsync(CancellationToken cancellationToken);

    // ---- used by the evaluator INSIDE the caller's transaction (the mold row is already locked); never opens its own.

    /// <summary>The mold has an open (Scheduled / In Progress) automatic PM.</summary>
    Task<bool> HasOpenAutomaticPmAsync(int moldId, CancellationToken cancellationToken);

    /// <summary>Issues the PM number (MOLD_PM sequence), inserts the PM and returns it with its id / row version, untracked.</summary>
    Task<MoldPm> AddAutomaticPmAsync(MoldPm pm, CancellationToken cancellationToken);

    // ---- workflow: one transaction each, mold row locked first (the same UPDLOCK Production Entry takes).

    /// <summary>
    /// Lock the mold, <paramref name="applyToLockedMold"/> (status), write the PM's start columns guarded by
    /// <paramref name="originalRowVersion"/> (stale -&gt; 409), save both, commit.
    /// </summary>
    Task<MoldPm> StartAsync(MoldPm pm, byte[] originalRowVersion, Action<Mold> applyToLockedMold, CancellationToken cancellationToken);

    /// <summary>
    /// Lock the mold, <paramref name="applyToLockedMold"/> (new cycle start, status), write the PM's completion columns
    /// guarded by <paramref name="originalRowVersion"/> (stale -&gt; 409), save both, then <paramref name="afterSave"/> (the
    /// evaluation of the new cycle) in the same transaction, commit.
    /// </summary>
    Task<MoldPm> CompleteAsync(
        MoldPm pm, byte[] originalRowVersion, Action<Mold> applyToLockedMold, Func<Mold, CancellationToken, Task> afterSave,
        CancellationToken cancellationToken);
}
