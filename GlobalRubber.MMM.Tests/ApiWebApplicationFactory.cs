using GlobalRubber.MMM.Application.Interfaces.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// Boots the real Api project in-memory for integration tests, pinned to the Development
/// environment so Swagger is available and configuration behaves the same way it does locally.
/// </summary>
public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");

        // The tests replace the repositories/services they call; no test host should reach for the database at start-up.
        builder.UseSetting("Database:WarmUpOnStartup", "false");
        builder.UseSetting("Notifications:MaintenanceScanEnabled", "false");

        // Workflows now publish notifications (migration 021): by default they go to memory, never to the configured
        // database. A test that inspects notifications registers its own repository (later registrations win).
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<INotificationRepository>();
            services.AddSingleton<INotificationRepository>(new InMemoryNotificationRepository());

            // Tests mint tokens for users / roles that exist in no database (TestAuth.MintToken): accept the token's own
            // role. SessionUserValidatorTests covers the real check; a test that needs it registers its own.
            services.RemoveAll<GlobalRubber.MMM.Application.Interfaces.ISessionUserValidator>();
            services.AddSingleton<GlobalRubber.MMM.Application.Interfaces.ISessionUserValidator>(new TokenRoleSessionUserValidator());
        });
    }
}

/// <summary>Test stand-in for the per-request user re-check: every token's user is active with the token's own role.</summary>
internal sealed class TokenRoleSessionUserValidator : GlobalRubber.MMM.Application.Interfaces.ISessionUserValidator
{
    public Task<string?> GetCurrentRoleCodeAsync(int userId, string? tokenRoleCode, CancellationToken cancellationToken) =>
        Task.FromResult(tokenRoleCode);
}

/// <summary>Records what a service published (never throws, like the real publisher); every notification is "Added".</summary>
internal sealed class RecordingNotificationPublisher : GlobalRubber.MMM.Application.Interfaces.INotificationPublisher
{
    public List<GlobalRubber.MMM.Application.Interfaces.NewNotification> Published { get; } = new();

    public Task<NotificationWriteResult> PublishAsync(GlobalRubber.MMM.Application.Interfaces.NewNotification notification, CancellationToken cancellationToken)
    {
        if (Published.Any(p => p.EventKey == notification.EventKey))
        {
            return Task.FromResult(NotificationWriteResult.AlreadyExists);
        }

        Published.Add(notification);
        return Task.FromResult(NotificationWriteResult.Added);
    }

    public Task<bool> HasBeenPublishedAsync(string eventKey, CancellationToken cancellationToken) =>
        Task.FromResult(Published.Any(p => p.EventKey == eventKey));
}
