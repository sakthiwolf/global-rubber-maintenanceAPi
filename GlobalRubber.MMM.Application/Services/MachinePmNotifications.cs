using System.Globalization;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// The Machine PM notifications (migration 021), in one place for every path that raises them: a PM occurrence was
/// scheduled (a plan's first occurrence, a new occurrence after an edit, the successor after a completion), its date has
/// come (Due) or passed (Overdue - the same "scheduled before today" the PM list calls overdue). Module TRN_MACHINE_PM:
/// delivered to the users whose role can View it. The message carries only the PM number, the machine code and the date.
/// One notification per event (UQ event_key): MachinePm{Scheduled|Due|Overdue}:{machine_pm_id}.
/// </summary>
internal static class MachinePmNotifications
{
    public const string PagePath = "/transactions/machine-maintenance";

    public static Task<NotificationWriteResult> ScheduledAsync(INotificationPublisher publisher, int machinePmId, string pmNo, string machineCode, DateOnly scheduledDate, int? actingUserId, CancellationToken cancellationToken) =>
        PublishAsync(publisher, NotificationTypes.MachinePmScheduled, NotificationSeverity.Info, "Preventive maintenance scheduled",
            $"{pmNo} is scheduled for {machineCode} on {Date(scheduledDate)}.", machinePmId, pmNo, actingUserId, cancellationToken);

    public static Task<NotificationWriteResult> DueAsync(INotificationPublisher publisher, int machinePmId, string pmNo, string machineCode, DateOnly scheduledDate, CancellationToken cancellationToken) =>
        PublishAsync(publisher, NotificationTypes.MachinePmDue, NotificationSeverity.Info, "Preventive maintenance due",
            $"{pmNo} is due today for {machineCode}.", machinePmId, pmNo, null, cancellationToken);

    public static Task<NotificationWriteResult> OverdueAsync(INotificationPublisher publisher, int machinePmId, string pmNo, string machineCode, DateOnly scheduledDate, CancellationToken cancellationToken) =>
        PublishAsync(publisher, NotificationTypes.MachinePmOverdue, NotificationSeverity.Warning, "Preventive maintenance overdue",
            $"{pmNo} for {machineCode} was due on {Date(scheduledDate)} and is overdue.", machinePmId, pmNo, null, cancellationToken);

    private static Task<NotificationWriteResult> PublishAsync(
        INotificationPublisher publisher, string type, string severity, string title, string message, int machinePmId, string pmNo,
        int? actingUserId, CancellationToken cancellationToken) =>
        publisher.PublishAsync(new NewNotification(
            type, ModuleCodes.TrnMachinePm, severity, title, message, $"{type}:{machinePmId}",
            EntityName: MachinePmAuditNames.EntityName, EntityId: machinePmId, RecordRef: pmNo, LinkPath: PagePath, CreatedBy: actingUserId),
            cancellationToken);

    private static string Date(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
