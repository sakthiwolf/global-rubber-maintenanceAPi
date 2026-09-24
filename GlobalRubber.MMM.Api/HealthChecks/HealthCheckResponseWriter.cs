using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GlobalRubber.MMM.Api.HealthChecks;

/// <summary>
/// Renders a <see cref="HealthReport"/> as JSON, consistently across <c>/health</c>,
/// <c>/liveness</c> and <c>/readiness</c>, instead of the default plain-text "Healthy"/"Unhealthy".
/// </summary>
public static class HealthCheckResponseWriter
{
    public static Task WriteJsonResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var payload = new
        {
            status = report.Status.ToString(),
            totalDurationMs = report.TotalDuration.TotalMilliseconds,
            checks = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new
                {
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    durationMs = entry.Value.Duration.TotalMilliseconds,
                }),
        };

        return context.Response.WriteAsJsonAsync(payload);
    }
}
