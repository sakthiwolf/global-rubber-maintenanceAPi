using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>An in-app notification as returned by GET /api/v1/notifications.</summary>
public sealed class NotificationDto
{
    public int NotificationId { get; init; }

    /// <summary>MoldPmWarning / MoldPmDue.</summary>
    public string NotificationType { get; init; } = string.Empty;

    public string ModuleCode { get; init; } = string.Empty;

    /// <summary>Info / Warning / Critical.</summary>
    public string Severity { get; init; } = string.Empty;

    public string Title { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? RecordRef { get; init; }

    /// <summary>The page the notification opens (a front-end route).</summary>
    public string? LinkPath { get; init; }

    public DateTime CreatedAt { get; init; }

    /// <summary>True when the system created it (no user).</summary>
    public bool CreatedBySystem { get; init; }
}

/// <summary>GET /api/v1/notifications query: paging only (newest first).</summary>
public sealed class NotificationListQuery : PaginationRequest
{
}
