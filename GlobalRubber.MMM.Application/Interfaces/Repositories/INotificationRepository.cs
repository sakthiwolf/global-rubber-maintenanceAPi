using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Interfaces.Repositories;

/// <summary>Outcome of <see cref="INotificationRepository.TryAddAsync"/>.</summary>
public enum NotificationWriteResult
{
    Added,

    /// <summary>A notification with the same event key already exists - the event was notified before.</summary>
    AlreadyExists,

    /// <summary>The insert failed for another reason; it was rolled back to a savepoint so the caller's work stands.</summary>
    Failed,
}

/// <summary>A notification as one user sees it: <see cref="ReadAt"/> is null while it is unread for that user.</summary>
public sealed record UserNotification(Notification Notification, DateTime? ReadAt);

public interface INotificationRepository
{
    Task<bool> ExistsAsync(string eventKey, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the notification inside the caller's transaction, behind a savepoint: a duplicate event key or any other
    /// failure is rolled back to the savepoint and reported - it never fails the caller's transaction.
    /// </summary>
    Task<NotificationWriteResult> TryAddAsync(Notification notification, CancellationToken cancellationToken);

    /// <summary>
    /// A page of the notifications of these modules with the user's read state (migration 018), newest first; filtered,
    /// sorted and paged in SQL. <paramref name="unreadOnly"/> keeps only those the user has not read.
    /// </summary>
    Task<(IReadOnlyList<UserNotification> Items, int TotalCount)> GetForUserAsync(
        int userId, IReadOnlyCollection<string> moduleCodes, bool unreadOnly, int pageNumber, int pageSize, CancellationToken cancellationToken);

    /// <summary>How many notifications of these modules the user has not read (a SQL COUNT).</summary>
    Task<int> CountUnreadAsync(int userId, IReadOnlyCollection<string> moduleCodes, CancellationToken cancellationToken);

    /// <summary>The notification with its read state for the user - null when it does not exist OR is not of these modules.</summary>
    Task<UserNotification?> GetVisibleAsync(int userId, int notificationId, IReadOnlyCollection<string> moduleCodes, CancellationToken cancellationToken);

    /// <summary>Records that the user read the notification; idempotent (an existing read keeps its original time).</summary>
    Task MarkReadAsync(int userId, int notificationId, DateTime readAt, CancellationToken cancellationToken);

    /// <summary>Marks every unread notification of these modules as read for the user in ONE SQL statement; returns how many.</summary>
    Task<int> MarkAllReadAsync(int userId, IReadOnlyCollection<string> moduleCodes, DateTime readAt, CancellationToken cancellationToken);
}
