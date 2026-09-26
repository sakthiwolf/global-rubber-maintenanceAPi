using System.Security.Claims;
using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace GlobalRubber.MMM.Api.Controllers;

/// <summary>
/// In-app notifications (migration 014). Self-service like the header bell / Alert Center (analysis 12.4): any
/// authenticated user reads THEIR notifications, which the service filters to the modules their role can View - so there
/// is no [RequirePermission] here (reviewed open endpoint in EndpointAuthorizationCoverageTests; 401 without a token).
/// </summary>
[Route("api/v1/notifications")]
public sealed class NotificationController : BaseApiController
{
    private readonly INotificationService _notificationService;

    public NotificationController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    /// <summary>A page of the signed-in user's notifications, newest first.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<NotificationDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<PagedResult<NotificationDto>>>> GetMine(
        [FromQuery] NotificationListQuery query, CancellationToken cancellationToken)
    {
        // Same as Auth.GetMyPermissions: no token / invalid token -> 401 in the standard ApiResponse shape.
        var userId = int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id)
            ? id
            : throw new UnauthorizedAccessException();

        var result = await _notificationService.GetMyNotificationsAsync(userId, query, cancellationToken);

        return Ok(ApiResponse<PagedResult<NotificationDto>>.Ok(result, "Notifications retrieved successfully."));
    }
}
