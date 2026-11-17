using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EduScroll.API.Health;

/// <summary>
/// Process liveness probe indicating the ASP.NET Core host can accept and handle HTTP requests.
/// </summary>
public sealed class SelfLivenessCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(HealthCheckResult.Healthy("Process is alive."));
    }
}
