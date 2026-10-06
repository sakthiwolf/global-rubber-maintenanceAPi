using System.Globalization;
using GlobalRubber.MMM.Application.Services;
using Microsoft.AspNetCore.SignalR;

namespace GlobalRubber.MMM.Api.Realtime;

/// <summary>
/// Sends the realtime "notifications changed" signal for whatever the request recorded in
/// <see cref="NotificationChangeSignal"/> - only AFTER the request completed successfully, so the business transaction that
/// wrote the notification has committed and a client that re-reads its count sees it. A failed request (exception or an
/// error status) signals nothing. New notifications go to every connected user (each re-reads its own, permission-filtered
/// count; the signal itself carries no data); a read-state change goes only to that user's connections (their other tabs).
/// A failure to push is logged and never affects the response.
/// </summary>
public sealed class NotificationSignalMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<NotificationSignalMiddleware> _logger;

    public NotificationSignalMiddleware(RequestDelegate next, ILogger<NotificationSignalMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, NotificationChangeSignal signal, IHubContext<NotificationHub> hub)
    {
        await _next(context);

        if (context.Response.StatusCode >= StatusCodes.Status400BadRequest
            || (!signal.HasNewNotifications && signal.ReadStateUsers.Count == 0))
        {
            return;
        }

        try
        {
            // Not the request's token: the request is over, and a client disconnecting must not cancel the push.
            if (signal.HasNewNotifications)
            {
                await hub.Clients.All.SendAsync(NotificationHub.NotificationsChangedMethod, CancellationToken.None);
            }
            else
            {
                var users = signal.ReadStateUsers.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToList();
                await hub.Clients.Users(users).SendAsync(NotificationHub.NotificationsChangedMethod, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "The realtime notification signal could not be sent.");
        }
    }
}
