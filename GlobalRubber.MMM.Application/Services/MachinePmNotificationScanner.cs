using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Raises the time-based Machine PM notifications (migration 021) that no user action triggers: an open occurrence whose
/// scheduled date is TODAY (plant date) gets "Preventive maintenance due"; one whose date has PASSED (the PM list's
/// "overdue") gets "Preventive maintenance overdue". Each at most once per occurrence (event key per PM). Run on a
/// schedule by the API host; each run handles at most <see cref="BatchSize"/> occurrences (the rest on the next run).
/// </summary>
public interface IMachinePmNotificationScanner
{
    /// <returns>How many notifications were written.</returns>
    Task<int> ScanAsync(CancellationToken cancellationToken);
}

public sealed class MachinePmNotificationScanner : IMachinePmNotificationScanner
{
    public const int BatchSize = 200;

    private readonly IMachinePmNotificationQuery _query;
    private readonly INotificationPublisher _publisher;
    private readonly IDateTimeProvider _dateTimeProvider;

    public MachinePmNotificationScanner(IMachinePmNotificationQuery query, INotificationPublisher publisher, IDateTimeProvider dateTimeProvider)
    {
        _query = query;
        _publisher = publisher;
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<int> ScanAsync(CancellationToken cancellationToken)
    {
        var today = _dateTimeProvider.Today; // plant (IST) date - the same "today" the PM list uses
        var written = 0;
        foreach (var pm in await _query.GetUnnotifiedDueAsync(today, BatchSize, cancellationToken))
        {
            var result = pm.ScheduledDate == today
                ? MachinePmNotifications.DueAsync(_publisher, pm.MachinePmId, pm.PmNo, pm.MachineCode, pm.ScheduledDate, cancellationToken)
                : MachinePmNotifications.OverdueAsync(_publisher, pm.MachinePmId, pm.PmNo, pm.MachineCode, pm.ScheduledDate, cancellationToken);
            if (await result == NotificationWriteResult.Added)
            {
                written++;
            }
        }

        return written;
    }
}
