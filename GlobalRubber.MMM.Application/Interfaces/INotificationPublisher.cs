using GlobalRubber.MMM.Application.Interfaces.Repositories;

namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// The one entry point business modules use to raise an in-app notification - they never touch the notification
/// repository. Delivery follows the existing rule (migration 014): every user whose role has View on
/// <see cref="NewNotification.ModuleCode"/> receives it. Idempotent per <see cref="NewNotification.EventKey"/>.
/// </summary>
public interface INotificationPublisher
{
    /// <summary>
    /// Writes the notification inside the caller's transaction (behind a savepoint). Never throws for a bad or duplicate
    /// notification and never fails the caller's work: the outcome is returned (and logged when it failed).
    /// </summary>
    Task<NotificationWriteResult> PublishAsync(NewNotification notification, CancellationToken cancellationToken);

    /// <summary>True when an event with this key has been notified before.</summary>
    Task<bool> HasBeenPublishedAsync(string eventKey, CancellationToken cancellationToken);
}

/// <summary>
/// A notification to publish. <see cref="NotificationType"/> / <see cref="Severity"/> must be values the database allows
/// (NotificationTypes / NotificationSeverity); <see cref="EventKey"/> identifies the business event (one notification per
/// event); <see cref="LinkPath"/> is the front-end route it opens; <see cref="CreatedBy"/> null = the system.
/// </summary>
public sealed record NewNotification(
    string NotificationType,
    string ModuleCode,
    string Severity,
    string Title,
    string Message,
    string EventKey,
    string? EntityName = null,
    int? EntityId = null,
    string? RecordRef = null,
    string? LinkPath = null,
    int? CreatedBy = null);
