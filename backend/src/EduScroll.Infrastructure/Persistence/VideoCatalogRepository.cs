using System.Linq.Expressions;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EduScroll.Infrastructure.Persistence;

public sealed class VideoCatalogRepository(ApplicationDbContext dbContext)
    : IVideoCatalogRepository
{
    private const int MaxSearchTerms = 12;
    private const int MaxUniqueConstraintRetries = 2;
    private const string YouTubeVideoIdIndexName = "IX_CatalogVideos_YouTubeVideoId";

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

        var validVideos = videos.Where(v => v is not null).ToArray();
        if (validVideos.Length == 0)
        {
            return 0;
        }

        var videoIds = validVideos
            .Select(v => v.YouTubeVideoId)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var strategy = dbContext.Database.CreateExecutionStrategy();

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await strategy.ExecuteAsync(async ct =>
                {
                    await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

                    try
                    {
                        var existingList = await dbContext.CatalogVideos
                            .Where(v => videoIds.Contains(v.YouTubeVideoId))
                            .ToListAsync(ct);

                        var trackedByVideoId = existingList.ToDictionary(
                            v => v.YouTubeVideoId,
                            StringComparer.OrdinalIgnoreCase);
                        var touchedVideoIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var incoming in validVideos)
                        {
                            if (trackedByVideoId.TryGetValue(incoming.YouTubeVideoId, out var target))
                            {
                                if (!ReferenceEquals(target, incoming))
                                {
                                    target.UpdateFromMetadata(
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
                            }
                            else
                            {
                                if (dbContext.Entry(incoming).State == EntityState.Detached)
                                {
                                    await dbContext.CatalogVideos.AddAsync(incoming, ct);
                                }

                                trackedByVideoId[incoming.YouTubeVideoId] = incoming;
                            }

                            touchedVideoIds.Add(incoming.YouTubeVideoId);
                        }

                        await dbContext.SaveChangesAsync(ct);
                        await transaction.CommitAsync(ct);

                        return touchedVideoIds.Count;
                    }
                    catch
                    {
                        dbContext.ChangeTracker.Clear();
                        throw;
                    }
                }, cancellationToken);
            }
            catch (DbUpdateException ex) when (
                attempt < MaxUniqueConstraintRetries &&
                IsUniqueConstraintViolation(ex, YouTubeVideoIdIndexName))
            {
                dbContext.ChangeTracker.Clear();
            }
        }
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

        IQueryable<CatalogVideo> query = dbContext.CatalogVideos.AsNoTracking();
        if (normalizedLanguages.Length == 1)
        {
            var singleLanguage = normalizedLanguages[0];
            query = query.Where(v => v.Language == singleLanguage);
        }
        else
        {
            var firstLanguage = normalizedLanguages[0];
            var secondLanguage = normalizedLanguages[1];
            query = query.Where(v => v.Language == firstLanguage || v.Language == secondLanguage);
        }

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

    internal static bool IsUniqueConstraintViolation(DbUpdateException ex, string indexOrConstraintName)
    {
        for (Exception? current = ex.InnerException; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlEx &&
                (sqlEx.Number == 2601 || sqlEx.Number == 2627) &&
                sqlEx.Message.Contains(indexOrConstraintName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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
            var capturedTerm = term;
            Expression<Func<string>> termLambda = () => capturedTerm;
            var termExpression = termLambda.Body;

            var titleContains = Expression.Call(titleProp, containsMethod, termExpression);
            var descContains = Expression.Call(descProp, containsMethod, termExpression);
            var termMatch = Expression.OrElse(titleContains, descContains);

            combined = combined is null ? termMatch : Expression.OrElse(combined, termMatch);
        }

        return Expression.Lambda<Func<CatalogVideo, bool>>(combined!, parameter);
    }
}
