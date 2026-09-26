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

public interface INotificationRepository
{
    Task<bool> ExistsAsync(string eventKey, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the notification inside the caller's transaction, behind a savepoint: a duplicate event key or any other
    /// failure is rolled back to the savepoint and reported - it never fails the caller's transaction.
    /// </summary>
    Task<NotificationWriteResult> TryAddAsync(Notification notification, CancellationToken cancellationToken);

    /// <summary>A page of the notifications for these modules, newest first.</summary>
    Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetForModulesAsync(
        IReadOnlyCollection<string> moduleCodes, int pageNumber, int pageSize, CancellationToken cancellationToken);
}
