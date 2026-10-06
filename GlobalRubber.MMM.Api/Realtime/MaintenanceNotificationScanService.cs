using GlobalRubber.MMM.Application.Services;
using Microsoft.AspNetCore.SignalR;

namespace GlobalRubber.MMM.Api.Realtime;

/// <summary>
/// Runs the time-based Machine PM notification scan (<see cref="IMachinePmNotificationScanner"/>: PM due today / overdue)
/// shortly after start-up and then every <see cref="Interval"/> - a server-side schedule, no client polling. When a run
/// wrote notifications, the connected clients get the same payload-free SignalR signal as for request-driven
/// notifications, so the bell updates without a refresh. A failed run is logged and retried on the next tick.
/// "Notifications:MaintenanceScanEnabled": false turns it off (the integration tests do).
/// </summary>
public sealed class MaintenanceNotificationScanService : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly ILogger<MaintenanceNotificationScanService> _logger;

    public MaintenanceNotificationScanService(IServiceScopeFactory scopeFactory, IHubContext<NotificationHub> hub, ILogger<MaintenanceNotificationScanService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRunDelay, stoppingToken);
            using var timer = new PeriodicTimer(Interval);
            do
            {
                await RunOnceAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down
        }
    }

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var written = await scope.ServiceProvider.GetRequiredService<IMachinePmNotificationScanner>().ScanAsync(cancellationToken);
            if (written > 0)
            {
                _logger.LogInformation("Machine PM notification scan wrote {Count} notification(s).", written);
                await _hub.Clients.All.SendAsync(NotificationHub.NotificationsChangedMethod, CancellationToken.None);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "The Machine PM notification scan failed; it is retried on the next run.");
        }
    }
}
