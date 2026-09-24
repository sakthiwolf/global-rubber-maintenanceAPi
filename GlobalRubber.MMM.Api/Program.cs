using GlobalRubber.MMM.Api.Bootstrap;
using GlobalRubber.MMM.Api.Extensions;
using GlobalRubber.MMM.Application;
using GlobalRubber.MMM.Infrastructure;

// "bootstrap-admin" runs the one-time initial-administrator console flow instead of the normal
// web API - Kestrel must never start for this path (BootstrapAdminCommandRunner builds its own
// minimal DI-only host). Anything else on the command line is left untouched for the normal
// WebApplication builder below.
if (args.Any(a => string.Equals(a, "bootstrap-admin", StringComparison.OrdinalIgnoreCase)))
{
    var exitCode = await BootstrapAdminCommandRunner.RunAsync();
    Environment.Exit(exitCode);
}

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddPresentation(builder.Configuration);

var app = builder.Build();

app.Logger.LogInformation(
    "Starting {ApplicationName} in {Environment} environment.",
    builder.Configuration["Application:Name"],
    app.Environment.EnvironmentName);

app.UsePresentation();

app.Run();

// Exposed for WebApplicationFactory<Program> in the integration test project.
public partial class Program
{
}
