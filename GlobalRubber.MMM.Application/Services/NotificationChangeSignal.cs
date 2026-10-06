using GlobalRubber.MMM.Application.Interfaces;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// Per-request (scoped) record of notification changes - see <see cref="INotificationChangeSignal"/>. Pure bookkeeping:
/// the presentation layer reads it once the request has succeeded and pushes the realtime signal (SignalR).
/// </summary>
public sealed class NotificationChangeSignal : INotificationChangeSignal
{
    private readonly HashSet<int> _readStateUsers = new();

    /// <summary>True once a notification was written during this request.</summary>
    public bool HasNewNotifications { get; private set; }

    /// <summary>The users whose read state changed during this request.</summary>
    public IReadOnlyCollection<int> ReadStateUsers => _readStateUsers;

    public void NotificationsCreated() => HasNewNotifications = true;

    public void ReadStateChanged(int userId) => _readStateUsers.Add(userId);
}
