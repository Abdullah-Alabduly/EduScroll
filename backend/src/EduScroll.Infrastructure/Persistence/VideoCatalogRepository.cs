using System.Linq.Expressions;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Domain.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EduScroll.Infrastructure.Persistence;

public sealed class VideoCatalogRepository(ApplicationDbContext dbContext)
    : IVideoCatalogRepository
{
    private const int MaxSearchTerms = 20;
    private const int DescriptionMatchPrefixLength = 400;
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
        CancellationToken cancellationToken,
        int? maxDurationSeconds = null)
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

        var cleanedTerms = (searchTerms ?? [])
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Select(t => t.Trim())
            .Select(t => t.Length > SourceSearchQuery.MaxQueryTextLength
                ? t[..SourceSearchQuery.MaxQueryTextLength]
                : t)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxSearchTerms)
            .ToArray();

        if (normalizedLanguages.Length == 1)
        {
            return await ExecuteSingleLanguageSearchAsync(
                normalizedLanguages[0],
                cleanedTerms,
                boundedMax,
                maxDurationSeconds,
                cancellationToken);
        }

        // Execute one indexed equality seek per language sequentially to preserve scoped DbContext safety.
        var combined = new List<CatalogVideo>(boundedMax * normalizedLanguages.Length);
        foreach (var language in normalizedLanguages)
        {
            var perLang = await ExecuteSingleLanguageSearchAsync(
                language,
                cleanedTerms,
                boundedMax,
                maxDurationSeconds,
                cancellationToken);
            combined.AddRange(perLang);
        }

        if (cleanedTerms.Length == 0)
        {
            return combined
                .OrderByDescending(v => v.PublishedAtUtc)
                .ThenByDescending(v => v.ViewCount)
                .Take(boundedMax)
                .ToArray();
        }

        return combined
            .OrderByDescending(v => CountDistinctTermMatches(v.Title, cleanedTerms))
            .ThenByDescending(v => CountDistinctTermMatches(GetDescriptionPrefix(v.Description), cleanedTerms))
            .ThenByDescending(v => v.PublishedAtUtc)
            .ThenByDescending(v => v.ViewCount)
            .Take(boundedMax)
            .ToArray();
    }

    private async Task<IReadOnlyList<CatalogVideo>> ExecuteSingleLanguageSearchAsync(
        string language,
        string[] cleanedTerms,
        int maxResults,
        int? maxDurationSeconds,
        CancellationToken cancellationToken)
    {
        IQueryable<CatalogVideo> query = dbContext.CatalogVideos
            .AsNoTracking()
            .Where(v => v.Language == language);

        if (maxDurationSeconds is > 0)
        {
            var maxDuration = maxDurationSeconds.Value;
            query = query.Where(v => !v.DurationSeconds.HasValue || v.DurationSeconds.Value <= maxDuration);
        }

        if (cleanedTerms.Length == 0)
        {
            return await query
                .OrderByDescending(v => v.PublishedAtUtc)
                .ThenByDescending(v => v.ViewCount)
                .Take(maxResults)
                .ToListAsync(cancellationToken);
        }

        var (filterExpr, titleScoreExpr, descScoreExpr) = BuildKeywordSearchExpressions(cleanedTerms);

        return await query
            .Where(filterExpr)
            .OrderByDescending(titleScoreExpr)
            .ThenByDescending(descScoreExpr)
            .ThenByDescending(v => v.PublishedAtUtc)
            .ThenByDescending(v => v.ViewCount)
            .Take(maxResults)
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

    private static (
        Expression<Func<CatalogVideo, bool>> Filter,
        Expression<Func<CatalogVideo, int>> TitleScore,
        Expression<Func<CatalogVideo, int>> DescriptionScore) BuildKeywordSearchExpressions(string[] terms)
    {
        var parameter = Expression.Parameter(typeof(CatalogVideo), "v");
        var titleProp = Expression.Property(parameter, nameof(CatalogVideo.Title));
        var descProp = Expression.Property(parameter, nameof(CatalogVideo.Description));
        var substringMethod = typeof(string).GetMethod(nameof(string.Substring), [typeof(int), typeof(int)])!;
        var descPrefixExpr = Expression.Call(
            descProp,
            substringMethod,
            Expression.Constant(0),
            Expression.Constant(DescriptionMatchPrefixLength));
        var containsMethod = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
        var oneExpr = Expression.Constant(1);
        var zeroExpr = Expression.Constant(0);

        Expression? anyTitleHit = null;
        Expression? titleScoreSum = null;
        Expression? descScoreSum = null;

        foreach (var term in terms)
        {
            var capturedTerm = term;
            Expression<Func<string>> termLambda = () => capturedTerm;
            var termExpression = termLambda.Body;

            var titleContains = Expression.Call(titleProp, containsMethod, termExpression);
            var descContains = Expression.Call(descPrefixExpr, containsMethod, termExpression);

            anyTitleHit = anyTitleHit is null ? titleContains : Expression.OrElse(anyTitleHit, titleContains);

            var titleHitScore = Expression.Condition(titleContains, oneExpr, zeroExpr);
            var descHitScore = Expression.Condition(descContains, oneExpr, zeroExpr);

            titleScoreSum = titleScoreSum is null ? titleHitScore : Expression.Add(titleScoreSum, titleHitScore);
            descScoreSum = descScoreSum is null ? descHitScore : Expression.Add(descScoreSum, descHitScore);
        }

        // Single-token queries require a Title match to prevent noisy single-word description matches;
        // multi-token queries match >= 1 token in Title OR >= 2 distinct tokens in the Description prefix.
        var filterBody = terms.Length == 1
            ? anyTitleHit!
            : Expression.OrElse(
                anyTitleHit!,
                Expression.GreaterThanOrEqual(descScoreSum!, Expression.Constant(2)));

        return (
            Expression.Lambda<Func<CatalogVideo, bool>>(filterBody, parameter),
            Expression.Lambda<Func<CatalogVideo, int>>(titleScoreSum!, parameter),
            Expression.Lambda<Func<CatalogVideo, int>>(descScoreSum!, parameter));
    }

    private static string GetDescriptionPrefix(string? description)
    {
        if (string.IsNullOrEmpty(description))
        {
            return string.Empty;
        }

        return description.Length <= DescriptionMatchPrefixLength
            ? description
            : description[..DescriptionMatchPrefixLength];
    }

    private static int CountDistinctTermMatches(string? text, string[] terms)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var count = 0;
        foreach (var term in terms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }
}
