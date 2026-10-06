using GlobalRubber.MMM.Application.Interfaces;
using GlobalRubber.MMM.Application.Interfaces.Repositories;
using GlobalRubber.MMM.Domain.Entities;
using GlobalRubber.MMM.Infrastructure.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Infrastructure.Repositories;

public sealed class NotificationRepository : INotificationRepository
{
    private const string Savepoint = "grm_notification";

    private readonly GlobalRubberDbContext _dbContext;
    private readonly INotificationChangeSignal _changeSignal;
    private readonly ILogger<NotificationRepository> _logger;

    public NotificationRepository(GlobalRubberDbContext dbContext, INotificationChangeSignal changeSignal, ILogger<NotificationRepository> logger)
    {
        _dbContext = dbContext;
        _changeSignal = changeSignal;
        _logger = logger;
    }

    public Task<bool> ExistsAsync(string eventKey, CancellationToken cancellationToken) =>
        _dbContext.Notifications.AnyAsync(n => n.EventKey == eventKey, cancellationToken);

    public async Task<NotificationWriteResult> TryAddAsync(Notification notification, CancellationToken cancellationToken)
    {
        // Behind a savepoint of the caller's transaction: whatever happens to this insert, the caller's own work (the
        // production entry, the PM) is kept. XACT_ABORT is off on these connections, so a failed statement does not doom
        // the transaction.
        var transaction = _dbContext.Database.CurrentTransaction;
        if (transaction is not null)
        {
            await transaction.CreateSavepointAsync(Savepoint, cancellationToken);
        }

        _dbContext.Notifications.Add(notification);
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            // Every notification insert passes through here (publisher and direct callers): record it so the clients are
            // told to refresh - AFTER the request (and so the caller's transaction) has completed, never from inside it.
            _changeSignal.NotificationsCreated();
            return NotificationWriteResult.Added;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (transaction is not null)
            {
                await transaction.RollbackToSavepointAsync(Savepoint, cancellationToken);
            }

            if (ex is DbUpdateException { InnerException: SqlException { Number: 2601 or 2627 } })
            {
                return NotificationWriteResult.AlreadyExists; // UQ_notification_transaction_event_key: notified before
            }

            _logger.LogError(ex, "Notification {EventKey} could not be written.", notification.EventKey);
            return NotificationWriteResult.Failed;
        }
        finally
        {
            _dbContext.Entry(notification).State = EntityState.Detached;
        }
    }

    public async Task<(IReadOnlyList<UserNotification> Items, int TotalCount)> GetForUserAsync(
        int userId, IReadOnlyCollection<string> moduleCodes, bool unreadOnly, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var query = Visible(moduleCodes);
        if (unreadOnly)
        {
            query = query.Where(n => !_dbContext.NotificationReads.Any(r => r.UserId == userId && r.NotificationId == n.NotificationId));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var rows = await query
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.NotificationId)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new
            {
                Notification = n,
                ReadAt = _dbContext.NotificationReads
                    .Where(r => r.UserId == userId && r.NotificationId == n.NotificationId)
                    .Select(r => (DateTime?)r.ReadAt)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return (rows.Select(r => new UserNotification(r.Notification, r.ReadAt)).ToList(), totalCount);
    }

    public Task<int> CountUnreadAsync(int userId, IReadOnlyCollection<string> moduleCodes, CancellationToken cancellationToken) =>
        Visible(moduleCodes)
            .CountAsync(n => !_dbContext.NotificationReads.Any(r => r.UserId == userId && r.NotificationId == n.NotificationId), cancellationToken);

    public async Task<UserNotification?> GetVisibleAsync(
        int userId, int notificationId, IReadOnlyCollection<string> moduleCodes, CancellationToken cancellationToken)
    {
        var row = await Visible(moduleCodes)
            .Where(n => n.NotificationId == notificationId)
            .Select(n => new
            {
                Notification = n,
                ReadAt = _dbContext.NotificationReads
                    .Where(r => r.UserId == userId && r.NotificationId == n.NotificationId)
                    .Select(r => (DateTime?)r.ReadAt)
                    .FirstOrDefault(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : new UserNotification(row.Notification, row.ReadAt);
    }

    public async Task MarkReadAsync(int userId, int notificationId, DateTime readAt, CancellationToken cancellationToken)
    {
        // Insert-if-absent in ONE statement; the lock hints make the existence check and the insert atomic, so two
        // concurrent calls (two tabs) cannot both insert. An existing read keeps its original read_at.
        await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO transactions.notification_read_transaction (user_id, notification_id, read_at)
SELECT {userId}, {notificationId}, {readAt}
WHERE NOT EXISTS (SELECT 1 FROM transactions.notification_read_transaction WITH (UPDLOCK, HOLDLOCK)
                  WHERE user_id = {userId} AND notification_id = {notificationId});", cancellationToken);
    }

    public async Task<int> MarkAllReadAsync(
        int userId, IReadOnlyCollection<string> moduleCodes, DateTime readAt, CancellationToken cancellationToken)
    {
        if (moduleCodes.Count == 0)
        {
            return 0;
        }

        // Set-based: every notification of the user's viewable modules without a read row gets one. The module list is
        // passed as ONE parameter and split in SQL (module codes never contain a comma - they are A-Z / _ codes).
        var codes = string.Join(",", moduleCodes);
        return await _dbContext.Database.ExecuteSqlInterpolatedAsync($@"
INSERT INTO transactions.notification_read_transaction (user_id, notification_id, read_at)
SELECT {userId}, n.notification_id, {readAt}
FROM transactions.notification_transaction n
WHERE n.module_code IN (SELECT value FROM STRING_SPLIT({codes}, ','))
  AND NOT EXISTS (SELECT 1 FROM transactions.notification_read_transaction r WITH (UPDLOCK, HOLDLOCK)
                  WHERE r.user_id = {userId} AND r.notification_id = n.notification_id);", cancellationToken);
    }

    private IQueryable<Notification> Visible(IReadOnlyCollection<string> moduleCodes) =>
        _dbContext.Notifications.AsNoTracking().Where(n => moduleCodes.Contains(n.ModuleCode));
}
