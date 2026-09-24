using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace GlobalRubber.MMM.Api.HealthChecks;

/// <summary>
/// Trivial "is the process alive" check used for the <c>/liveness</c> endpoint. It never touches
/// the database or any dependency - if this can't report Healthy, the process itself is in
/// trouble, which is exactly what a liveness probe (e.g. a container orchestrator) needs to know
/// before deciding whether to restart the instance.
/// </summary>
public sealed class SelfHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(HealthCheckResult.Healthy("The API process is running."));
}
