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
    private readonly ILogger<NotificationRepository> _logger;

    public NotificationRepository(GlobalRubberDbContext dbContext, ILogger<NotificationRepository> logger)
    {
        _dbContext = dbContext;
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

    public async Task<(IReadOnlyList<Notification> Items, int TotalCount)> GetForModulesAsync(
        IReadOnlyCollection<string> moduleCodes, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        var query = _dbContext.Notifications.AsNoTracking().Where(n => moduleCodes.Contains(n.ModuleCode));
        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.NotificationId)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
