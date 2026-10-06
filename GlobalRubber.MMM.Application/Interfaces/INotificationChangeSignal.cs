namespace GlobalRubber.MMM.Application.Interfaces;

/// <summary>
/// Records, during one request, that notification data changed so connected clients can be told to refresh their unread
/// count (SignalR, instead of polling). It only RECORDS the change: the signal is sent after the request has completed
/// successfully - i.e. after the business transaction that wrote the notification has committed - never from inside it.
/// The signal carries no notification data; every client re-reads its own, permission-filtered count.
/// </summary>
public interface INotificationChangeSignal
{
    /// <summary>A new notification was written: every signed-in user may have a new unread one.</summary>
    void NotificationsCreated();

    /// <summary>The user's read state changed (one or all marked read): the user's other tabs refresh.</summary>
    void ReadStateChanged(int userId);
}
