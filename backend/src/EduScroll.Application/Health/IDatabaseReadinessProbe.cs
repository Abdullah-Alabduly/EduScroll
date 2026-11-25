namespace EduScroll.Application.Health;

/// <summary>
/// Checks whether the backing database is reachable for application readiness probes.
/// </summary>
public interface IDatabaseReadinessProbe
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}
