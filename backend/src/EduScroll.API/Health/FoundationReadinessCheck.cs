using EduScroll.API.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace EduScroll.API.Health;

/// <summary>
/// Sprint 1 readiness probe verifying core foundation dependencies actually implemented in this sprint.
/// Does NOT check PostgreSQL yet; Sprint 2 will register real database readiness checks here.
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
