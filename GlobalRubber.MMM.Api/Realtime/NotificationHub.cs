using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GlobalRubber.MMM.Api.Realtime;

/// <summary>
/// Realtime channel for in-app notifications (replaces the frontend's unread-count polling). Signed-in users only (the
/// same JWT as the REST API; browsers send it as the access_token query value on this path). The server only ever sends
/// <see cref="NotificationsChangedMethod"/> - with NO payload - and the client then re-reads its own unread count through
/// GET /api/v1/notifications/unread-count, so the permission rules stay in one place and nothing leaks over the socket.
/// Clients call nothing on the hub.
/// </summary>
[Authorize]
public sealed class NotificationHub : Hub
{
    /// <summary>Route of the hub.</summary>
    public const string Path = "/hubs/notifications";

    /// <summary>Client method: "your notifications may have changed - re-read the unread count".</summary>
    public const string NotificationsChangedMethod = "notificationsChanged";
}
