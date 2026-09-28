using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Constants;
using GlobalRubber.MMM.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Validates a notification against the database's own limits (CK_notification_transaction_type / _severity, the column
/// lengths) and writes it through the repository's savepoint insert. An invalid notification is a programming error in
/// the calling module: it is logged and reported as Failed, never thrown into the caller's business transaction.
/// </summary>
public sealed class NotificationPublisher : INotificationPublisher
{
    private readonly INotificationRepository _repository;
    private readonly ILogger<NotificationPublisher> _logger;

    public NotificationPublisher(INotificationRepository repository, ILogger<NotificationPublisher> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<NotificationWriteResult> PublishAsync(NewNotification notification, CancellationToken cancellationToken)
    {
        var problems = Validate(notification);
        if (problems.Count > 0)
        {
            _logger.LogError("Notification {EventKey} was not written: {Problems}", notification.EventKey, string.Join(" ", problems));
            return NotificationWriteResult.Failed;
        }

        return await _repository.TryAddAsync(new Notification
        {
            NotificationType = notification.NotificationType,
            ModuleCode = notification.ModuleCode,
            Severity = notification.Severity,
            Title = notification.Title.Trim(),
            Message = notification.Message.Trim(),
            EntityName = notification.EntityName,
            EntityId = notification.EntityId,
            RecordRef = notification.RecordRef,
            LinkPath = notification.LinkPath,
            EventKey = notification.EventKey,
            CreatedBy = notification.CreatedBy,
        }, cancellationToken);
    }

    public Task<bool> HasBeenPublishedAsync(string eventKey, CancellationToken cancellationToken) =>
        _repository.ExistsAsync(eventKey, cancellationToken);

    // The limits are exactly the table's (migration 014): nothing stricter is invented here.
    private static List<string> Validate(NewNotification n)
    {
        var problems = new List<string>();
        if (!NotificationTypes.All.Contains(n.NotificationType)) problems.Add($"Unknown notification type '{n.NotificationType}'.");
        if (!NotificationSeverity.All.Contains(n.Severity)) problems.Add($"Unknown severity '{n.Severity}'.");
        Required(n.ModuleCode, "ModuleCode", 50);
        Required(n.Title, "Title", 150);
        Required(n.Message, "Message", 500);
        Required(n.EventKey, "EventKey", 100);
        Optional(n.EntityName, "EntityName", 50);
        Optional(n.RecordRef, "RecordRef", 50);
        Optional(n.LinkPath, "LinkPath", 200);
        if (n.LinkPath is { Length: > 0 } link && !link.StartsWith('/')) problems.Add("LinkPath must be an application route starting with '/'.");
        return problems;

        void Required(string? value, string name, int max)
        {
            if (string.IsNullOrWhiteSpace(value)) problems.Add($"{name} is required.");
            else if (value.Trim().Length > max) problems.Add($"{name} must be at most {max} characters.");
        }

        void Optional(string? value, string name, int max)
        {
            if (value is not null && value.Length > max) problems.Add($"{name} must be at most {max} characters.");
        }
    }
}
