using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// The signed-in user's notifications. The user id always comes from the caller's token (never from the request), and
/// a user only ever sees - and can only mark - the notifications of the modules their role can View.
/// </summary>
public interface INotificationService
{
    /// <summary>The signed-in user's notifications: those of every module their role can View (never a role-name check).</summary>
    Task<PagedResult<NotificationDto>> GetMyNotificationsAsync(int userId, NotificationListQuery query, CancellationToken cancellationToken);

    /// <summary>How many of those the user has not read.</summary>
    Task<NotificationUnreadCountDto> GetMyUnreadCountAsync(int userId, CancellationToken cancellationToken);

    /// <summary>Marks one of the user's notifications as read (idempotent). 404 when it is not one of theirs.</summary>
    Task<NotificationDto> MarkAsReadAsync(int userId, int notificationId, CancellationToken cancellationToken);

    /// <summary>Marks every one of the user's notifications as read.</summary>
    Task<NotificationMarkAllReadResultDto> MarkAllAsReadAsync(int userId, CancellationToken cancellationToken);
}
