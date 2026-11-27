using EduScroll.Application.Exceptions;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduScroll.Infrastructure.Persistence;

public sealed class WhitelistedChannelRepository(ApplicationDbContext dbContext)
    : IWhitelistedChannelRepository
{
    private const int MaxUniqueConstraintRetries = 2;
    private const string YouTubeChannelIdIndexName = "IX_WhitelistedChannels_YouTubeChannelId";

    public Task<WhitelistedChannel?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        if (id == Guid.Empty)
        {
            return Task.FromResult<WhitelistedChannel?>(null);
        }

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

    public async Task<IReadOnlyList<WhitelistedChannel>> ListAsync(
        string? language,
        bool? isActive,
        CancellationToken cancellationToken)
    {
        var query = dbContext.WhitelistedChannels.AsNoTracking();

        if (isActive.HasValue)
        {
            query = query.Where(c => c.IsActive == isActive.Value);
        }

        if (!string.IsNullOrWhiteSpace(language))
        {
            var normalizedLang = language.Trim().ToLowerInvariant();
            query = query.Where(c => c.Language == normalizedLang);
        }

        return await query
            .OrderByDescending(c => c.IsActive)
            .ThenByDescending(c => c.TrustScore)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(
        WhitelistedChannel channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var strategy = dbContext.Database.CreateExecutionStrategy();

        try
        {
            await strategy.ExecuteAsync(async ct =>
            {
                try
                {
                    var exists = await dbContext.WhitelistedChannels
                        .AsNoTracking()
                        .AnyAsync(c => c.YouTubeChannelId == channel.YouTubeChannelId, ct);

                    if (exists)
                    {
                        throw new ConflictAppException(
                            $"A whitelisted channel with YouTube channel ID '{channel.YouTubeChannelId}' already exists.",
                            "CHANNEL_ALREADY_EXISTS");
                    }

                    if (dbContext.Entry(channel).State == EntityState.Detached)
                    {
                        await dbContext.WhitelistedChannels.AddAsync(channel, ct);
                    }

                    await dbContext.SaveChangesAsync(ct);
                }
                catch
                {
                    dbContext.ChangeTracker.Clear();
                    throw;
                }
            }, cancellationToken);
        }
        catch (DbUpdateException ex) when (
            VideoCatalogRepository.IsUniqueConstraintViolation(ex, YouTubeChannelIdIndexName))
        {
            dbContext.ChangeTracker.Clear();
            throw new ConflictAppException(
                $"A whitelisted channel with YouTube channel ID '{channel.YouTubeChannelId}' already exists.",
                "CHANNEL_ALREADY_EXISTS");
        }
    }

    public async Task AddOrUpdateAsync(
        WhitelistedChannel channel,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(channel);

        var strategy = dbContext.Database.CreateExecutionStrategy();

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await strategy.ExecuteAsync(async ct =>
                {
                    try
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
                    }
                    catch
                    {
                        dbContext.ChangeTracker.Clear();
                        throw;
                    }
                }, cancellationToken);

                return;
            }
            catch (DbUpdateException ex) when (
                attempt < MaxUniqueConstraintRetries &&
                VideoCatalogRepository.IsUniqueConstraintViolation(ex, YouTubeChannelIdIndexName))
            {
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    public async Task MarkSyncedAsync(
        Guid channelId,
        DateTimeOffset syncedAtUtc,
        CancellationToken cancellationToken)
    {
        if (channelId == Guid.Empty)
        {
            return;
        }

        var normalizedSyncedAtUtc = syncedAtUtc.ToUniversalTime();
        await dbContext.WhitelistedChannels
            .Where(c => c.Id == channelId)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(c => c.LastSyncedAtUtc, normalizedSyncedAtUtc),
                cancellationToken);
    }
}
