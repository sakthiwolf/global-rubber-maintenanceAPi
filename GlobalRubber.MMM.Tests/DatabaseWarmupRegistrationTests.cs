using GlobalRubber.MMM.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GlobalRubber.MMM.Tests;

/// <summary>
/// The database warm-up (DatabaseWarmupService) is registered only when there is a database to warm up and it has not
/// been switched off. Checked on the service collection alone - no host is started and no database is touched.
/// </summary>
public class DatabaseWarmupRegistrationTests
{
    private const string WarmupTypeName = "DatabaseWarmupService";

    private static bool IsRegistered(Dictionary<string, string?> settings)
    {
        settings["Jwt:Issuer"] = "test";
        settings["Jwt:Audience"] = "test";
        settings["Jwt:Secret"] = new string('x', 32);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        return services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType?.Name == WarmupTypeName);
    }

    [Fact]
    public void Registered_ByDefault_WhenAConnectionStringIsConfigured()
    {
        Assert.True(IsRegistered(new() { ["ConnectionStrings:DefaultConnection"] = "Server=.;Database=x" }));
    }

    [Fact]
    public void NotRegistered_WhenSwitchedOff()
    {
        Assert.False(IsRegistered(new()
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=.;Database=x",
            ["Database:WarmUpOnStartup"] = "false",
        }));
    }

    [Fact]
    public void NotRegistered_WithoutAConnectionString()
    {
        Assert.False(IsRegistered(new()));
    }

    [Fact]
    public void TestHost_HasNoWarmup()
    {
        using var factory = new ApiWebApplicationFactory();

        var hosted = factory.Services.GetServices<IHostedService>();

        Assert.DoesNotContain(hosted, h => h.GetType().Name == WarmupTypeName);
    }
}
