using GlobalRubber.MMM.Api.Configuration;
using GlobalRubber.MMM.Api.HealthChecks;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GlobalRubber.MMM.Api.Extensions;

/// <summary>
/// Presentation-layer HTTP pipeline configuration, kept out of <c>Program.cs</c> for the same
/// reason as <see cref="ServiceCollectionExtensions"/>.
/// </summary>
public static class WebApplicationExtensions
{
    public static WebApplication UsePresentation(this WebApplication app)
    {
        app.UseExceptionHandler();

        // Always in Development; elsewhere only when Swagger:Enabled is true (appsettings.json).
        var swagger = app.Configuration.GetSection(SwaggerOptions.SectionName).Get<SwaggerOptions>() ?? new SwaggerOptions();
        if (app.Environment.IsDevelopment() || swagger.Enabled)
        {
            app.UseSwagger();
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint($"/swagger/{swagger.Version}/swagger.json", $"{swagger.Title} {swagger.Version}");
            });
        }

        app.UseCors(ServiceCollectionExtensions.CorsPolicyName);

        if (!app.Environment.IsDevelopment())
        {
            app.UseHttpsRedirection();
        }

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        app.MapHealthEndpoints();

        return app;
    }

    /// <summary>
    /// Maps the three health endpoints the foundation requires:
    /// <c>/health</c> (everything), <c>/liveness</c> (process only), <c>/readiness</c>
    /// (dependencies, currently just the database).
    /// </summary>
    private static WebApplication MapHealthEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = HealthCheckResponseWriter.WriteJsonResponse,
        });

        app.MapHealthChecks("/liveness", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
            ResponseWriter = HealthCheckResponseWriter.WriteJsonResponse,
        });

        app.MapHealthChecks("/readiness", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = HealthCheckResponseWriter.WriteJsonResponse,
        });

        return app;
    }
}
