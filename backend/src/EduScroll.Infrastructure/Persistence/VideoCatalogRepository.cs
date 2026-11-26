using System.Linq.Expressions;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EduScroll.Infrastructure.Persistence;

public sealed class VideoCatalogRepository(ApplicationDbContext dbContext)
    : IVideoCatalogRepository
{
    private const int MaxSearchTerms = 12;

    public Task<CatalogVideo?> GetByYouTubeVideoIdAsync(
        string youTubeVideoId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(youTubeVideoId))
        {
            return Task.FromResult<CatalogVideo?>(null);
        }

        var trimmed = youTubeVideoId.Trim();
        return dbContext.CatalogVideos
            .AsNoTracking()
            .FirstOrDefaultAsync(v => v.YouTubeVideoId == trimmed, cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogVideo>> GetByYouTubeVideoIdsAsync(
        IReadOnlyCollection<string> youTubeVideoIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(youTubeVideoIds);

        var distinctIds = youTubeVideoIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctIds.Length == 0)
        {
            return [];
        }

        return await dbContext.CatalogVideos
            .AsNoTracking()
            .Where(v => distinctIds.Contains(v.YouTubeVideoId))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> UpsertRangeAsync(
        IReadOnlyList<CatalogVideo> videos,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(videos);

        if (videos.Count == 0)
        {
            return 0;
        }

        // Deduplicate batch by YouTubeVideoId (case-insensitive to stay safe under SQL Server CI collation).
        var deduped = new Dictionary<string, CatalogVideo>(StringComparer.OrdinalIgnoreCase);
        foreach (var video in videos)
        {
            if (video is not null)
            {
                deduped[video.YouTubeVideoId] = video;
            }
        }

        if (deduped.Count == 0)
        {
            return 0;
        }

        var videoIds = deduped.Keys.ToArray();
        var strategy = dbContext.Database.CreateExecutionStrategy();

        return await strategy.ExecuteAsync(async ct =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

            var existingList = await dbContext.CatalogVideos
                .Where(v => videoIds.Contains(v.YouTubeVideoId))
                .ToListAsync(ct);

            var existingByVideoId = existingList.ToDictionary(
                v => v.YouTubeVideoId,
                StringComparer.OrdinalIgnoreCase);

            foreach (var incoming in deduped.Values)
            {
                if (existingByVideoId.TryGetValue(incoming.YouTubeVideoId, out var existing))
                {
                    existing.UpdateFromMetadata(
                        incoming.WhitelistedChannelId,
                        incoming.ChannelExternalId,
                        incoming.ChannelName,
                        incoming.Title,
                        incoming.Description,
                        incoming.ThumbnailUrl,
                        incoming.Language,
                        incoming.DurationSeconds,
                        incoming.ViewCount,
                        incoming.LikeCount,
                        incoming.PublishedAtUtc,
                        incoming.DiscoverySource,
                        incoming.UpdatedAtUtc);
                }
                else
                {
                    if (dbContext.Entry(incoming).State == EntityState.Detached)
                    {
                        await dbContext.CatalogVideos.AddAsync(incoming, ct);
                    }
                }
            }

            await dbContext.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            return deduped.Count;
        }, cancellationToken);
    }

    public async Task<IReadOnlyList<CatalogVideo>> SearchByLanguageAsync(
        IReadOnlyCollection<string> languages,
        IReadOnlyCollection<string>? searchTerms,
        int maxResults,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(languages);

        var normalizedLanguages = languages
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Trim().ToLowerInvariant())
            .Where(l => l is SourceSearchQuery.EnglishCode or SourceSearchQuery.ArabicCode)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedLanguages.Length == 0 || maxResults <= 0)
        {
            return [];
        }

        var boundedMax = Math.Clamp(maxResults, 1, 200);

        IQueryable<CatalogVideo> query = dbContext.CatalogVideos
            .AsNoTracking()
            .Where(v => normalizedLanguages.Contains(v.Language));

        var cleanedTerms = (searchTerms ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSearchTerms)
            .ToArray();

        if (cleanedTerms.Length > 0)
        {
            query = query.Where(BuildKeywordFilterExpression(cleanedTerms));
        }

        return await query
            .OrderByDescending(v => v.PublishedAtUtc)
            .ThenByDescending(v => v.ViewCount)
            .Take(boundedMax)
            .ToListAsync(cancellationToken);
    }

    private static Expression<Func<CatalogVideo, bool>> BuildKeywordFilterExpression(string[] terms)
    {
        var parameter = Expression.Parameter(typeof(CatalogVideo), "v");
        var titleProp = Expression.Property(parameter, nameof(CatalogVideo.Title));
        var descProp = Expression.Property(parameter, nameof(CatalogVideo.Description));
        var containsMethod = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;

        Expression? combined = null;

        foreach (var term in terms)
        {
            var termConstant = Expression.Constant(term, typeof(string));
            var titleContains = Expression.Call(titleProp, containsMethod, termConstant);
            var descContains = Expression.Call(descProp, containsMethod, termConstant);
            var termMatch = Expression.OrElse(titleContains, descContains);

            combined = combined is null ? termMatch : Expression.OrElse(combined, termMatch);
        }

        return Expression.Lambda<Func<CatalogVideo, bool>>(combined!, parameter);
    }
}
