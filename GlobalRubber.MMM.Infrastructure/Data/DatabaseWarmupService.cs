using GlobalRubber.MMM.Application.Interfaces.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GlobalRubber.MMM.Infrastructure.Data;

/// <summary>
/// Pays EF Core's one-time, per-process start-up cost as soon as the process starts instead of inside the first user
/// request (slow endpoint investigation, 2026-09-28). Measured on a fresh process: building the EF model 1.5-2.4 s, first
/// SQL connection ~0.3-0.4 s, first query compilation ~0.5 s - the ~2.8 s that whichever request came first used to wait
/// for (the Mold Maintenance page fires three requests at once, so all three showed ~2.8 s).
///
/// Nothing is cached and no business data is read: it builds the model, opens (and returns to the pool) one connection,
/// and runs the permission lookup every [RequirePermission] endpoint runs first, for a role code that cannot exist.
/// Runs in the background: it never delays start-up, and a failure (e.g. the database is unreachable) is only logged -
/// the first real request then simply does the same work itself, exactly as before.
/// </summary>
internal sealed class DatabaseWarmupService : BackgroundService
{
    private const string NonExistentRoleCode = "__WARMUP__"; // role_code is at most 30 characters; no role uses this

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DatabaseWarmupService> _logger;

    public DatabaseWarmupService(IServiceScopeFactory scopeFactory, ILogger<DatabaseWarmupService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // the model build is synchronous CPU work - keep it off the host's start-up path

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<GlobalRubberDbContext>();

            _ = dbContext.Model;
            await dbContext.Database.OpenConnectionAsync(stoppingToken);
            await dbContext.Database.CloseConnectionAsync();
            await scope.ServiceProvider.GetRequiredService<IPermissionRepository>()
                .GetPermissionFlagsAsync(NonExistentRoleCode, NonExistentRoleCode, stoppingToken);

            _logger.LogInformation("Database warm-up completed.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host shutting down.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Database warm-up failed; the first request will initialise the database access instead.");
        }
    }
}
