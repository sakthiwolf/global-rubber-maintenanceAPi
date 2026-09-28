using GlobalRubber.MMM.Application.Common;

namespace GlobalRubber.MMM.Application.DTOs;

/// <summary>An in-app notification as returned by GET /api/v1/notifications.</summary>
public sealed class NotificationDto
{
    public int NotificationId { get; init; }

    /// <summary>MoldPmWarning / MoldPmDue / SparePartLowStock / SparePartOutOfStock.</summary>
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

    /// <summary>Whether the SIGNED-IN user has read it (migration 018 - per user, not per browser).</summary>
    public bool IsRead { get; init; }

    /// <summary>When the signed-in user read it (UTC); null while unread.</summary>
    public DateTime? ReadAt { get; init; }
}

/// <summary>GET /api/v1/notifications query: paging (newest first) and an optional unread-only filter.</summary>
public sealed class NotificationListQuery : PaginationRequest
{
    /// <summary>true = only the notifications the signed-in user has not read.</summary>
    public bool UnreadOnly { get; set; }
}

/// <summary>GET /api/v1/notifications/unread-count.</summary>
public sealed class NotificationUnreadCountDto
{
    public int UnreadCount { get; init; }
}

/// <summary>PATCH /api/v1/notifications/read-all.</summary>
public sealed class NotificationMarkAllReadResultDto
{
    /// <summary>How many notifications this call marked as read.</summary>
    public int MarkedCount { get; init; }

    /// <summary>The unread count afterwards (0 unless new notifications arrived meanwhile).</summary>
    public int UnreadCount { get; init; }
}
