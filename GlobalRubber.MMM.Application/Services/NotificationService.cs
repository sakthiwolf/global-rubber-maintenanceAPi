using GlobalRubber.MMM.Application.Common;
using GlobalRubber.MMM.Application.DTOs;
using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;

namespace GlobalRubber.MMM.Application.Services;

/// <summary>
/// In-app notifications (migration 014). A user sees the notifications of every module their role can View - the same
/// permission matrix as everything else, never a role name. Read state stays on the client (analysis Q-31 still open).
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly INotificationRepository _repository;
    private readonly IPermissionService _permissionService;

    public NotificationService(INotificationRepository repository, IPermissionService permissionService)
    {
        _repository = repository;
        _permissionService = permissionService;
    }

    public async Task<PagedResult<NotificationDto>> GetMyNotificationsAsync(int userId, NotificationListQuery query, CancellationToken cancellationToken)
    {
        var matrix = await _permissionService.GetMyPermissionsAsync(userId, cancellationToken);
        var viewable = matrix.Permissions.Where(p => p.CanView).Select(p => p.ModuleCode).ToList();

        if (viewable.Count == 0)
        {
            return PagedResult<NotificationDto>.Create(Array.Empty<NotificationDto>(), query.PageNumber, query.PageSize, 0);
        }

        var (items, totalCount) = await _repository.GetForModulesAsync(viewable, query.PageNumber, query.PageSize, cancellationToken);

        return PagedResult<NotificationDto>.Create(items.Select(n => new NotificationDto
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
        }).ToList(), query.PageNumber, query.PageSize, totalCount);
    }
}
