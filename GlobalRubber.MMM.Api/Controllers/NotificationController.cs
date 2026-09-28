using System.Security.Claims;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// In-app notifications (migrations 014 / 018). Self-service like the header bell / Alert Center (analysis 12.4): any
/// authenticated user reads and marks THEIR notifications, which the service filters to the modules their role can View -
/// so there is no [RequirePermission] here (reviewed open endpoints in EndpointAuthorizationCoverageTests; 401 without a
/// token). The user id always comes from the token, never from the request. There is deliberately NO create endpoint:
/// notifications are raised by business modules through INotificationPublisher.
/// </summary>
[Route("api/v1/notifications")]
public sealed class NotificationController : BaseApiController
{
    private readonly INotificationService _notificationService;

    public NotificationController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    /// <summary>A page of the signed-in user's notifications, newest first (optionally unread only), with their read state.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<NotificationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PagedResult<NotificationDto>>>> GetMine(
        [FromQuery] NotificationListQuery query, CancellationToken cancellationToken)
    {
        var result = await _notificationService.GetMyNotificationsAsync(CurrentUserId(), query, cancellationToken);

        return Ok(ApiResponse<PagedResult<NotificationDto>>.Ok(result, "Notifications retrieved successfully."));
    }

    /// <summary>How many of the signed-in user's notifications are unread (the bell badge).</summary>
    [HttpGet("unread-count")]
    [ProducesResponseType(typeof(ApiResponse<NotificationUnreadCountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<NotificationUnreadCountDto>>> GetMyUnreadCount(CancellationToken cancellationToken)
    {
        var result = await _notificationService.GetMyUnreadCountAsync(CurrentUserId(), cancellationToken);

        return Ok(ApiResponse<NotificationUnreadCountDto>.Ok(result, "Unread notification count retrieved successfully."));
    }

    /// <summary>Marks one of the signed-in user's notifications as read (idempotent). 404 when it is not one of theirs.</summary>
    [HttpPatch("{notificationId:int}/read")]
    [ProducesResponseType(typeof(ApiResponse<NotificationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ApiResponse<NotificationDto>>> MarkAsRead(int notificationId, CancellationToken cancellationToken)
    {
        var result = await _notificationService.MarkAsReadAsync(CurrentUserId(), notificationId, cancellationToken);

        return Ok(ApiResponse<NotificationDto>.Ok(result, "Notification marked as read."));
    }

    /// <summary>Marks every one of the signed-in user's notifications as read.</summary>
    [HttpPatch("read-all")]
    [ProducesResponseType(typeof(ApiResponse<NotificationMarkAllReadResultDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<NotificationMarkAllReadResultDto>>> MarkAllAsRead(CancellationToken cancellationToken)
    {
        var result = await _notificationService.MarkAllAsReadAsync(CurrentUserId(), cancellationToken);

        return Ok(ApiResponse<NotificationMarkAllReadResultDto>.Ok(result, "All notifications marked as read."));
    }

    // Same as Auth.GetMyPermissions: no token / invalid token -> 401 in the standard ApiResponse shape.
    private int CurrentUserId() =>
        int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : throw new UnauthorizedAccessException();
}
