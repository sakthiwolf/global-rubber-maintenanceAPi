namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to transactions.notification_transaction (migration 014): an in-app notification. Immutable once written (no
/// row version, no update columns). Recipients are the users whose role has View on <see cref="ModuleCode"/> - never a
/// role name. <see cref="EventKey"/> is UNIQUE, so one event (e.g. a mold's warning for one PM cycle) is notified once.
/// CreatedBy NULL = created by the system.
/// </summary>
public class Notification
{
    public int NotificationId { get; set; }
    public string NotificationType { get; set; } = string.Empty;
    public string ModuleCode { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? EntityName { get; set; }
    public int? EntityId { get; set; }
    public string? RecordRef { get; set; }
    public string? LinkPath { get; set; }
    public string EventKey { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public int? CreatedBy { get; set; }
}
