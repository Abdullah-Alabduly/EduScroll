using EduScroll.API.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EduScroll.API.Health;

/// <summary>
/// Readiness probe verifying core API foundation options and runtime services.
/// SQL Server database connectivity is verified separately by <see cref="DatabaseReadinessCheck"/>.
/// </summary>
public sealed class FoundationReadinessCheck(
    TimeProvider timeProvider,
    IOptions<ApiMetadataOptions> apiOptions,
    IOptions<CorsOptions> corsOptions) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _ = timeProvider.GetUtcNow();
        _ = apiOptions.Value;
        _ = corsOptions.Value;

        return Task.FromResult(HealthCheckResult.Healthy("API foundation is ready."));
    }
}
