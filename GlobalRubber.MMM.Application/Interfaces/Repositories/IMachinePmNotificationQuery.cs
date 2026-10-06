namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

/// <summary>
/// The open Machine PM occurrences whose date has come and that have not been notified for it yet (migration 021):
/// scheduled TODAY without a MachinePmDue notification, or scheduled BEFORE today without a MachinePmOverdue one.
/// </summary>
public interface IMachinePmNotificationQuery
{
    Task<IReadOnlyList<MachinePmNotificationCandidate>> GetUnnotifiedDueAsync(DateOnly today, int maxCount, CancellationToken cancellationToken);
}

public sealed record MachinePmNotificationCandidate(int MachinePmId, string PmNo, string MachineCode, DateOnly ScheduledDate);
