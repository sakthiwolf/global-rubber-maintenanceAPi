namespace GlobalRubber.MMM.Domain.Entities;

/// <summary>
/// Maps to transactions.notification_read_transaction (migration 018): that <see cref="UserId"/> has read
/// <see cref="NotificationId"/>. Insert-only; a notification without a row for a user is unread for that user.
/// </summary>
public class NotificationRead
{
    public int UserId { get; set; }
    public int NotificationId { get; set; }
    public DateTime ReadAt { get; set; }
}
