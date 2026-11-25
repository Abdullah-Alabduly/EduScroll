using EduScroll.Application.Health;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EduScroll.API.Health;

/// <summary>
/// Readiness probe verifying SQL Server database connectivity.
/// </summary>
public sealed class DatabaseReadinessCheck(IDatabaseReadinessProbe readinessProbe) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var isReady = await readinessProbe.IsReadyAsync(cancellationToken);
        return isReady
            ? HealthCheckResult.Healthy("Database connection is ready.")
            : HealthCheckResult.Unhealthy("Database connection is unavailable.");
    }
}
