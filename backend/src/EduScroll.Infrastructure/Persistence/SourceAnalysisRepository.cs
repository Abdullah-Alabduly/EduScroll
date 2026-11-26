using EduScroll.Application.AiAnalysis.Ports;
using EduScroll.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduScroll.Infrastructure.Persistence;

public sealed class SourceAnalysisRepository(ApplicationDbContext dbContext) : ISourceAnalysisRepository
{
    public Task<SourceAnalysis?> GetBySourceIdForOwnerAsync(
        Guid sourceId,
        Guid ownerUserId,
        CancellationToken cancellationToken)
    {
        return dbContext.SourceAnalyses
            .AsNoTracking()
            .AsSplitQuery()
            .Include(a => a.Topics)
            .Include(a => a.SearchQueries)
            .FirstOrDefaultAsync(
                a => a.SourceId == sourceId && a.OwnerUserId == ownerUserId,
                cancellationToken);
    }

    public async Task SaveOrReplaceAsync(
        SourceAnalysis analysis,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        var strategy = dbContext.Database.CreateExecutionStrategy();

        await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

            // Replace any existing analysis for this owner + source atomically.
            await dbContext.SourceAnalyses
                .Where(a => a.SourceId == analysis.SourceId && a.OwnerUserId == analysis.OwnerUserId)
                .ExecuteDeleteAsync(ct);

            if (dbContext.Entry(analysis).State == EntityState.Detached)
            {
                await dbContext.SourceAnalyses.AddAsync(analysis, ct);
            }

            await dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }, cancellationToken);
    }
}
