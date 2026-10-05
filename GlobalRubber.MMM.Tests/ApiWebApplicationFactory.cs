using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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
    }
}
