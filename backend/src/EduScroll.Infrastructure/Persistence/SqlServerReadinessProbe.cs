using EduScroll.Application.Health;
using Microsoft.Extensions.Logging;

namespace EduScroll.Infrastructure.Persistence;

public sealed class SqlServerReadinessProbe(
    ApplicationDbContext dbContext,
    ILogger<SqlServerReadinessProbe> logger) : IDatabaseReadinessProbe
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(ProbeTimeout);

        try
        {
            var canConnect = await dbContext.Database.CanConnectAsync(timeoutCts.Token);
            if (!canConnect)
            {
                logger.LogWarning("SQL Server readiness probe could not establish a database connection.");
            }

            return canConnect;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SQL Server readiness check failed.");
            return false;
        }
    }
}
