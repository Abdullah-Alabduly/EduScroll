using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduScroll.Infrastructure.Persistence;

public sealed class WhitelistedChannelRepository(ApplicationDbContext dbContext)
    : IWhitelistedChannelRepository
{
    public Task<WhitelistedChannel?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return dbContext.WhitelistedChannels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
    }

    public Task<WhitelistedChannel?> GetByYouTubeChannelIdAsync(
        string youTubeChannelId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(youTubeChannelId))
        {
            return Task.FromResult<WhitelistedChannel?>(null);
        }

        var trimmed = youTubeChannelId.Trim();
        return dbContext.WhitelistedChannels
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.YouTubeChannelId == trimmed, cancellationToken);
    }

    public async Task<IReadOnlyList<WhitelistedChannel>> GetActiveAsync(
        string? language,
        CancellationToken cancellationToken)
    {
        var query = dbContext.WhitelistedChannels
            .AsNoTracking()
            .Where(c => c.IsActive);

        if (!string.IsNullOrWhiteSpace(language))
        {
            var normalizedLang = language.Trim().ToLowerInvariant();
            query = query.Where(c => c.Language == normalizedLang);
        }

        return await query
            .OrderByDescending(c => c.TrustScore)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddOrUpdateAsync(
        WhitelistedChannel channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async ct =>
        {
            var existing = await dbContext.WhitelistedChannels
                .FirstOrDefaultAsync(c => c.YouTubeChannelId == channel.YouTubeChannelId, ct);

            if (existing is null)
            {
                if (dbContext.Entry(channel).State == EntityState.Detached)
                {
                    await dbContext.WhitelistedChannels.AddAsync(channel, ct);
                }
            }
            else
            {
                existing.UpdateMetadata(
                    channel.Name,
                    channel.Language,
                    channel.TopicCategory,
                    channel.TrustScore,
                    channel.IsActive);
            }

            await dbContext.SaveChangesAsync(ct);
        }, cancellationToken);
    }

    public async Task MarkSyncedAsync(
        Guid channelId,
        DateTimeOffset syncedAtUtc,
        CancellationToken cancellationToken)
    {
        await dbContext.WhitelistedChannels
            .Where(c => c.Id == channelId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(c => c.LastSyncedAtUtc, syncedAtUtc),
                cancellationToken);
    }
}
