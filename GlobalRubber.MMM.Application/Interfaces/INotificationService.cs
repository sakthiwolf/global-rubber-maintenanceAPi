using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;

namespace GlobalRubber.MMM.Application.Interfaces;

public interface INotificationService
{
    /// <summary>The signed-in user's notifications: those of every module their role can View (never a role-name check).</summary>
    Task<PagedResult<NotificationDto>> GetMyNotificationsAsync(int userId, NotificationListQuery query, CancellationToken cancellationToken);
}
