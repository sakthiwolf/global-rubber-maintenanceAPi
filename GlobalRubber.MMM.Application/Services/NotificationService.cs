using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// In-app notifications (migrations 014 / 018). A user sees the notifications of every module their role can View - the
/// same permission matrix as everything else, never a role name - and read state is kept PER USER on the server
/// (notification_read_transaction). The user id is always the caller's own (from the token): a notification outside the
/// user's modules is reported as not found, so its existence is never revealed and it can never be marked.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _repository;
    private readonly IPermissionService _permissionService;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly INotificationChangeSignal _changeSignal;

    public NotificationService(
        INotificationRepository repository, IPermissionService permissionService, IDateTimeProvider dateTimeProvider, INotificationChangeSignal changeSignal)
    {
        _repository = repository;
        _permissionService = permissionService;
        _dateTimeProvider = dateTimeProvider;
        _changeSignal = changeSignal;
    }

    public async Task<PagedResult<NotificationDto>> GetMyNotificationsAsync(int userId, NotificationListQuery query, CancellationToken cancellationToken)
    {
        var viewable = await ViewableModulesAsync(userId, cancellationToken);
        if (viewable.Count == 0)
        {
            return PagedResult<NotificationDto>.Create(Array.Empty<NotificationDto>(), query.PageNumber, query.PageSize, 0);
        }

        var (items, totalCount) = await _repository.GetForUserAsync(userId, viewable, query.UnreadOnly, query.PageNumber, query.PageSize, cancellationToken);

        return PagedResult<NotificationDto>.Create(items.Select(MapToDto).ToList(), query.PageNumber, query.PageSize, totalCount);
    }

    public async Task<NotificationUnreadCountDto> GetMyUnreadCountAsync(int userId, CancellationToken cancellationToken)
    {
        var viewable = await ViewableModulesAsync(userId, cancellationToken);

        return new NotificationUnreadCountDto
        {
            UnreadCount = viewable.Count == 0 ? 0 : await _repository.CountUnreadAsync(userId, viewable, cancellationToken),
        };
    }

    public async Task<NotificationDto> MarkAsReadAsync(int userId, int notificationId, CancellationToken cancellationToken)
    {
        var viewable = await ViewableModulesAsync(userId, cancellationToken);

        // Not one of the user's notifications (unknown id, or a module the role cannot View) -> 404, never 403: the
        // response must not reveal that another module's notification exists.
        var visible = (viewable.Count == 0 ? null : await _repository.GetVisibleAsync(userId, notificationId, viewable, cancellationToken))
            ?? throw new NotFoundException(nameof(Notification), notificationId);

        if (visible.ReadAt is not null)
        {
            return MapToDto(visible); // already read - idempotent, the original read time is kept
        }

        await _repository.MarkReadAsync(userId, notificationId, _dateTimeProvider.UtcNow, cancellationToken);
        _changeSignal.ReadStateChanged(userId); // the user's other tabs refresh their badge

        var reread = await _repository.GetVisibleAsync(userId, notificationId, viewable, cancellationToken)
            ?? throw new NotFoundException(nameof(Notification), notificationId);
        return MapToDto(reread);
    }

    public async Task<NotificationMarkAllReadResultDto> MarkAllAsReadAsync(int userId, CancellationToken cancellationToken)
    {
        var viewable = await ViewableModulesAsync(userId, cancellationToken);
        if (viewable.Count == 0)
        {
            return new NotificationMarkAllReadResultDto { MarkedCount = 0, UnreadCount = 0 };
        }

        var marked = await _repository.MarkAllReadAsync(userId, viewable, _dateTimeProvider.UtcNow, cancellationToken);
        if (marked > 0)
        {
            _changeSignal.ReadStateChanged(userId);
        }

        return new NotificationMarkAllReadResultDto
        {
            MarkedCount = marked,
            UnreadCount = await _repository.CountUnreadAsync(userId, viewable, cancellationToken),
        };
    }

    private async Task<IReadOnlyCollection<string>> ViewableModulesAsync(int userId, CancellationToken cancellationToken)
    {
        var matrix = await _permissionService.GetMyPermissionsAsync(userId, cancellationToken);
        return matrix.Permissions.Where(p => p.CanView).Select(p => p.ModuleCode).Distinct().ToList();
    }

    private static NotificationDto MapToDto(UserNotification item)
    {
        var n = item.Notification;
        return new NotificationDto
        {
            NotificationId = n.NotificationId,
            NotificationType = n.NotificationType,
            ModuleCode = n.ModuleCode,
            Severity = n.Severity,
            Title = n.Title,
            Message = n.Message,
            RecordRef = n.RecordRef,
            LinkPath = n.LinkPath,
            CreatedAt = n.CreatedAt,
            CreatedBySystem = n.CreatedBy is null,
            IsRead = item.ReadAt is not null,
            ReadAt = item.ReadAt,
        };
    }
}
