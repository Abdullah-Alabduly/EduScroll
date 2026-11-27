using EduScroll.Application.Exceptions;
using EduScroll.Application.VideoDiscovery.Contracts;
using EduScroll.Application.VideoDiscovery.Models;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Domain.Entities;
using EduScroll.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EduScroll.Application.VideoDiscovery.Services;

/// <summary>
/// Database-First YouTube video discovery engine.
/// Queries the local SQL Server catalog first, falls back to YouTube <c>search.list</c>
/// only when local candidates are below threshold, enriches via <c>videos.list</c>,
/// persists write-through results, and degrades gracefully on quota or upstream errors.
/// </summary>
public sealed class VideoDiscoveryEngine(
    IVideoCatalogRepository videoCatalogRepository,
    IYouTubeSearchFallbackClient searchFallbackClient,
    IYouTubeVideoMetadataClient videoMetadataClient,
    VideoDiscoveryLimits limits,
    TimeProvider timeProvider,
    ILogger<VideoDiscoveryEngine> logger) : IVideoDiscoveryEngine
{
    private const int MaxDesiredCandidateCount = 100;
    private const int MaxSearchTermsPerLanguage = 20;
    private const int MaxFallbackQueriesCap = 5;
    private const int MaxFallbackResultsCap = 25;
    private const int MaxMetadataBatchSize = 50;
    private const string FallbackDisabledReason = "FALLBACK_DISABLED";
    private const string DefaultFallbackErrorReason = "YOUTUBE_FALLBACK_FAILED";

    public async Task<VideoDiscoveryResultDto> DiscoverAsync(
        VideoDiscoveryRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (desiredCount, enQueries, arQueries) = ValidateAndNormalizeRequest(request);
        var threshold = Math.Max(1, limits.MinLocalCandidatesThreshold);
        var fetchLimit = Math.Max(desiredCount, threshold);
        var maxDurationSeconds = ResolveMaxDurationSeconds(limits.MaxVideoDurationSeconds);

        // Stage 1: Database-First local SQL Server catalog lookup (sequential to respect scoped DbContext).
        var localVideos = (await QueryLocalCatalogAsync(
                request.TargetFeedLanguage,
                enQueries,
                arQueries,
                request.AdditionalKeywords,
                fetchLimit,
                maxDurationSeconds,
                cancellationToken))
            .Where(v => IsWithinMaxDuration(v, maxDurationSeconds))
            .ToList();

        if (localVideos.Count >= threshold)
        {
            var sufficientCandidates = localVideos
                .Take(desiredCount)
                .Select(CatalogVideoMappingHelper.MapToCandidateDto)
                .ToArray();

            logger.LogInformation(
                "Database-first discovery satisfied locally. TargetFeedLanguage={TargetFeedLanguage} LocalCandidates={LocalCandidates} Threshold={Threshold} Returned={Returned}",
                request.TargetFeedLanguage,
                localVideos.Count,
                threshold,
                sufficientCandidates.Length);

            return new VideoDiscoveryResultDto(
                Candidates: sufficientCandidates,
                LocalCandidateCount: sufficientCandidates.Length,
                FallbackCandidateCount: 0,
                FallbackTriggered: false,
                FallbackSkippedReason: null);
        }

        // Stage 2: Check whether live search.list fallback is enabled.
        if (!limits.EnableSearchFallback)
        {
            var localOnlyCandidates = localVideos
                .Take(desiredCount)
                .Select(CatalogVideoMappingHelper.MapToCandidateDto)
                .ToArray();

            logger.LogInformation(
                "Database-first discovery below threshold with fallback disabled. TargetFeedLanguage={TargetFeedLanguage} LocalCandidates={LocalCandidates} Threshold={Threshold}",
                request.TargetFeedLanguage,
                localVideos.Count,
                threshold);

            return new VideoDiscoveryResultDto(
                Candidates: localOnlyCandidates,
                LocalCandidateCount: localOnlyCandidates.Length,
                FallbackCandidateCount: 0,
                FallbackTriggered: false,
                FallbackSkippedReason: FallbackDisabledReason);
        }

        // Stage 3: Bounded search.list fallback + videos.list enrichment + write-through cache.
        var (fallbackItems, fallbackErrorReason) = await ExecuteSearchFallbackAsync(
            request.TargetFeedLanguage,
            enQueries,
            arQueries,
            cancellationToken);

        var existingVideoIds = new HashSet<string>(
            localVideos.Select(v => v.YouTubeVideoId),
            StringComparer.OrdinalIgnoreCase);

        var newFallbackItems = new List<YouTubeSearchFallbackItem>(fallbackItems.Count);
        foreach (var item in fallbackItems)
        {
            if (string.IsNullOrWhiteSpace(item.YouTubeVideoId))
            {
                continue;
            }

            var normalizedId = item.YouTubeVideoId.Trim();
            if (existingVideoIds.Add(normalizedId))
            {
                newFallbackItems.Add(item with { YouTubeVideoId = normalizedId });
            }
        }

        if (newFallbackItems.Count == 0)
        {
            var localOnlyCandidates = localVideos
                .Take(desiredCount)
                .Select(CatalogVideoMappingHelper.MapToCandidateDto)
                .ToArray();

            return new VideoDiscoveryResultDto(
                Candidates: localOnlyCandidates,
                LocalCandidateCount: localOnlyCandidates.Length,
                FallbackCandidateCount: 0,
                FallbackTriggered: true,
                FallbackSkippedReason: fallbackErrorReason);
        }

        var metadataByVideoId = await EnrichFallbackMetadataAsync(newFallbackItems, cancellationToken);
        var fallbackCatalogVideos = BuildFallbackCatalogVideos(newFallbackItems, metadataByVideoId);

        if (fallbackCatalogVideos.Count > 0)
        {
            await PersistWriteThroughAsync(fallbackCatalogVideos, cancellationToken);
        }

        var eligibleFallbackVideos = fallbackCatalogVideos
            .Where(v => IsWithinMaxDuration(v, maxDurationSeconds))
            .ToList();

        var localUsedCount = Math.Min(localVideos.Count, desiredCount);
        var remainingSlots = desiredCount - localUsedCount;

        var localDtos = localVideos
            .Take(localUsedCount)
            .Select(CatalogVideoMappingHelper.MapToCandidateDto);

        var fallbackDtos = eligibleFallbackVideos
            .Take(remainingSlots)
            .Select(CatalogVideoMappingHelper.MapToCandidateDto)
            .ToArray();

        var mergedCandidates = localDtos.Concat(fallbackDtos).ToArray();

        logger.LogInformation(
            "Database-first discovery completed with fallback. TargetFeedLanguage={TargetFeedLanguage} LocalUsed={LocalUsed} FallbackNewCached={FallbackCached} FallbackUsed={FallbackUsed} SkippedReason={SkippedReason}",
            request.TargetFeedLanguage,
            localUsedCount,
            fallbackCatalogVideos.Count,
            fallbackDtos.Length,
            fallbackErrorReason ?? "none");

        return new VideoDiscoveryResultDto(
            Candidates: mergedCandidates,
            LocalCandidateCount: localUsedCount,
            FallbackCandidateCount: fallbackDtos.Length,
            FallbackTriggered: true,
            FallbackSkippedReason: fallbackErrorReason);
    }

    private static (
        int DesiredCount,
        IReadOnlyList<VideoDiscoveryQueryInput> EnQueries,
        IReadOnlyList<VideoDiscoveryQueryInput> ArQueries) ValidateAndNormalizeRequest(VideoDiscoveryRequest request)
    {
        if (!Enum.IsDefined(request.TargetFeedLanguage))
        {
            throw new ValidationAppException(
                "Target feed language must be En, Ar, or Both.",
                new Dictionary<string, string[]>
                {
                    ["targetFeedLanguage"] = ["Allowed values are En, Ar, and Both."]
                },
                "VIDEO_DISCOVERY_INVALID_TARGET_LANGUAGE");
        }

        if (request.DesiredCandidateCount < 1)
        {
            throw new ValidationAppException(
                "Desired candidate count must be at least 1.",
                new Dictionary<string, string[]>
                {
                    ["desiredCandidateCount"] = ["DesiredCandidateCount must be at least 1."]
                },
                "VIDEO_DISCOVERY_INVALID_CANDIDATE_COUNT");
        }

        if (request.SearchQueries is null || request.SearchQueries.Count == 0)
        {
            throw new ValidationAppException(
                "At least one search query is required for video discovery.",
                new Dictionary<string, string[]>
                {
                    ["searchQueries"] = ["At least one search query is required."]
                },
                "VIDEO_DISCOVERY_QUERIES_REQUIRED");
        }

        var enQueries = new List<VideoDiscoveryQueryInput>();
        var arQueries = new List<VideoDiscoveryQueryInput>();
        var seenEn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenAr = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawQuery in request.SearchQueries)
        {
            if (rawQuery is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(rawQuery.Language))
            {
                throw new ValidationAppException(
                    "Search query language is required and must be 'en' or 'ar'.",
                    new Dictionary<string, string[]>
                    {
                        ["language"] = ["Allowed values are 'en' and 'ar'."]
                    },
                    "VIDEO_DISCOVERY_INVALID_LANGUAGE");
            }

            var normalizedLang = rawQuery.Language.Trim().ToLowerInvariant();
            if (normalizedLang is not (SourceSearchQuery.EnglishCode or SourceSearchQuery.ArabicCode))
            {
                throw new ValidationAppException(
                    "Search query language must be 'en' or 'ar'.",
                    new Dictionary<string, string[]>
                    {
                        ["language"] = ["Allowed values are 'en' and 'ar'."]
                    },
                    "VIDEO_DISCOVERY_INVALID_LANGUAGE");
            }

            if (!MatchesTargetFeedLanguage(normalizedLang, request.TargetFeedLanguage))
            {
                continue;
            }

            var normalizedQuery = NormalizeWhitespace(rawQuery.Query);
            if (string.IsNullOrEmpty(normalizedQuery))
            {
                continue;
            }

            normalizedQuery = CatalogVideoMappingHelper.TruncateSurrogateSafe(
                normalizedQuery,
                SourceSearchQuery.MaxQueryTextLength);

            if (normalizedLang == SourceSearchQuery.EnglishCode && seenEn.Add(normalizedQuery))
            {
                enQueries.Add(new VideoDiscoveryQueryInput(normalizedQuery, SourceSearchQuery.EnglishCode));
            }
            else if (normalizedLang == SourceSearchQuery.ArabicCode && seenAr.Add(normalizedQuery))
            {
                arQueries.Add(new VideoDiscoveryQueryInput(normalizedQuery, SourceSearchQuery.ArabicCode));
            }
        }

        if (enQueries.Count == 0 && arQueries.Count == 0)
        {
            throw new ValidationAppException(
                "At least one non-empty search query matching the target feed language is required.",
                new Dictionary<string, string[]>
                {
                    ["searchQueries"] = ["Provide at least one non-empty query for the requested target language."]
                },
                "VIDEO_DISCOVERY_QUERIES_REQUIRED");
        }

        var desiredCount = Math.Min(request.DesiredCandidateCount, MaxDesiredCandidateCount);
        return (desiredCount, enQueries, arQueries);
    }

    private async Task<IReadOnlyList<CatalogVideo>> QueryLocalCatalogAsync(
        TargetFeedLanguage targetLanguage,
        IReadOnlyList<VideoDiscoveryQueryInput> enQueries,
        IReadOnlyList<VideoDiscoveryQueryInput> arQueries,
        IReadOnlyList<string>? additionalKeywords,
        int fetchLimit,
        int maxDurationSeconds,
        CancellationToken cancellationToken)
    {
        if (targetLanguage == TargetFeedLanguage.En)
        {
            var enTerms = SearchQueryTokenizer.BuildSanitizedTerms(enQueries, additionalKeywords, MaxSearchTermsPerLanguage);
            if (enTerms.Length == 0)
            {
                return [];
            }

            var enVideos = await videoCatalogRepository.SearchByLanguageAsync(
                [SourceSearchQuery.EnglishCode],
                enTerms,
                fetchLimit,
                cancellationToken,
                maxDurationSeconds);
            return DeduplicateCatalogVideos(enVideos);
        }

        if (targetLanguage == TargetFeedLanguage.Ar)
        {
            var arTerms = SearchQueryTokenizer.BuildSanitizedTerms(arQueries, additionalKeywords, MaxSearchTermsPerLanguage);
            if (arTerms.Length == 0)
            {
                return [];
            }

            var arVideos = await videoCatalogRepository.SearchByLanguageAsync(
                [SourceSearchQuery.ArabicCode],
                arTerms,
                fetchLimit,
                cancellationToken,
                maxDurationSeconds);
            return DeduplicateCatalogVideos(arVideos);
        }

        // TargetFeedLanguage.Both: run sequentially per language and interleave for balanced coverage.
        IReadOnlyList<CatalogVideo> enResults = [];
        if (enQueries.Count > 0)
        {
            var enTerms = SearchQueryTokenizer.BuildSanitizedTerms(enQueries, additionalKeywords, MaxSearchTermsPerLanguage);
            if (enTerms.Length > 0)
            {
                enResults = await videoCatalogRepository.SearchByLanguageAsync(
                    [SourceSearchQuery.EnglishCode],
                    enTerms,
                    fetchLimit,
                    cancellationToken,
                    maxDurationSeconds);
            }
        }

        IReadOnlyList<CatalogVideo> arResults = [];
        if (arQueries.Count > 0)
        {
            var arTerms = SearchQueryTokenizer.BuildSanitizedTerms(arQueries, additionalKeywords, MaxSearchTermsPerLanguage);
            if (arTerms.Length > 0)
            {
                arResults = await videoCatalogRepository.SearchByLanguageAsync(
                    [SourceSearchQuery.ArabicCode],
                    arTerms,
                    fetchLimit,
                    cancellationToken,
                    maxDurationSeconds);
            }
        }

        return InterleaveAndDeduplicate(enResults, arResults, v => v.YouTubeVideoId);
    }

    private async Task<(IReadOnlyList<YouTubeSearchFallbackItem> Items, string? ErrorReason)> ExecuteSearchFallbackAsync(
        TargetFeedLanguage targetLanguage,
        IReadOnlyList<VideoDiscoveryQueryInput> enQueries,
        IReadOnlyList<VideoDiscoveryQueryInput> arQueries,
        CancellationToken cancellationToken)
    {
        var maxQueries = Math.Clamp(limits.MaxFallbackQueriesPerDiscovery, 1, MaxFallbackQueriesCap);
        var maxResultsPerQuery = Math.Clamp(limits.MaxFallbackResultsPerQuery, 1, MaxFallbackResultsCap);

        var selectedQueries = SelectFallbackQueries(targetLanguage, enQueries, arQueries, maxQueries);
        var enItems = new List<YouTubeSearchFallbackItem>();
        var arItems = new List<YouTubeSearchFallbackItem>();
        string? errorReason = null;

        foreach (var queryInput in selectedQueries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var results = await searchFallbackClient.SearchVideosAsync(
                    queryInput.Query,
                    queryInput.Language,
                    maxResultsPerQuery,
                    cancellationToken) ?? [];

                foreach (var item in results)
                {
                    if (item is null || string.IsNullOrWhiteSpace(item.YouTubeVideoId))
                    {
                        continue;
                    }

                    var normalizedLang = CatalogVideoMappingHelper.ResolveVideoLanguage(
                        item.Language,
                        queryInput.Language);
                    var normalizedItem = item with { Language = normalizedLang };

                    if (normalizedLang == SourceSearchQuery.ArabicCode)
                    {
                        arItems.Add(normalizedItem);
                    }
                    else
                    {
                        enItems.Add(normalizedItem);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var code = ex is AppException appEx ? appEx.ErrorCode : DefaultFallbackErrorReason;
                errorReason ??= code;

                logger.LogWarning(
                    "YouTube search fallback failed for query; degrading gracefully. Language={Language} ErrorCode={ErrorCode} ExceptionType={ExceptionType}",
                    queryInput.Language,
                    code,
                    ex.GetType().Name);

                if (IsTerminalFallbackErrorCode(code))
                {
                    break;
                }
            }
        }

        IReadOnlyList<YouTubeSearchFallbackItem> mergedItems = targetLanguage == TargetFeedLanguage.Both
            ? InterleaveAndDeduplicate(enItems, arItems, item => item.YouTubeVideoId)
            : DeduplicateItems(enItems.Concat(arItems), item => item.YouTubeVideoId);

        return (mergedItems, errorReason);
    }

    private async Task<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>> EnrichFallbackMetadataAsync(
        IReadOnlyList<YouTubeSearchFallbackItem> fallbackItems,
        CancellationToken cancellationToken)
    {
        var batchSize = Math.Clamp(limits.MetadataBatchSize, 1, MaxMetadataBatchSize);
        var metadataByVideoId = new Dictionary<string, YouTubeVideoMetadataItem>(
            fallbackItems.Count,
            StringComparer.OrdinalIgnoreCase);

        foreach (var batch in fallbackItems.Select(i => i.YouTubeVideoId).Chunk(batchSize))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var batchMetadata = await videoMetadataClient.GetVideoMetadataAsync(batch, cancellationToken);
                if (batchMetadata is null)
                {
                    continue;
                }

                foreach (var kvp in batchMetadata)
                {
                    if (!string.IsNullOrWhiteSpace(kvp.Key) && kvp.Value is not null)
                    {
                        metadataByVideoId[kvp.Key.Trim()] = kvp.Value;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                var code = ex is AppException appEx ? appEx.ErrorCode : "YOUTUBE_METADATA_ENRICHMENT_FAILED";
                logger.LogWarning(
                    "Metadata enrichment failed for fallback batch; keeping snippet metadata. BatchSize={BatchSize} ErrorCode={ErrorCode} ExceptionType={ExceptionType}",
                    batch.Length,
                    code,
                    ex.GetType().Name);
            }
        }

        return metadataByVideoId;
    }

    private List<CatalogVideo> BuildFallbackCatalogVideos(
        IReadOnlyList<YouTubeSearchFallbackItem> fallbackItems,
        IReadOnlyDictionary<string, YouTubeVideoMetadataItem> metadataByVideoId)
    {
        var now = timeProvider.GetUtcNow();
        var results = new List<CatalogVideo>(fallbackItems.Count);

        foreach (var fallbackItem in fallbackItems)
        {
            metadataByVideoId.TryGetValue(fallbackItem.YouTubeVideoId, out var meta);

            var title = CatalogVideoMappingHelper.ResolveRequiredText(
                meta?.Title,
                fallbackItem.Title,
                null,
                CatalogVideo.MaxTitleLength);
            var channelExternalId = CatalogVideoMappingHelper.ResolveRequiredText(
                meta?.ChannelExternalId,
                fallbackItem.ChannelExternalId,
                null,
                CatalogVideo.MaxChannelExternalIdLength);
            var channelName = CatalogVideoMappingHelper.ResolveRequiredText(
                meta?.ChannelName,
                fallbackItem.ChannelName,
                channelExternalId,
                CatalogVideo.MaxChannelNameLength);
            var description = !string.IsNullOrWhiteSpace(meta?.Description)
                ? meta.Description
                : fallbackItem.Description;
            var thumbnailUrl = CatalogVideoMappingHelper.ResolveThumbnailUrl(
                meta?.ThumbnailUrl,
                fallbackItem.ThumbnailUrl);
            var language = CatalogVideoMappingHelper.ResolveVideoLanguage(
                meta?.DefaultLanguage,
                fallbackItem.Language);
            var durationSeconds = meta?.DurationSeconds is >= 0 ? meta.DurationSeconds : null;
            var viewCount = Math.Max(0L, meta?.ViewCount ?? 0L);
            var likeCount = Math.Max(0L, meta?.LikeCount ?? 0L);
            var publishedAtUtc = meta?.PublishedAtUtc is { } metaPublished && metaPublished != default
                ? metaPublished
                : fallbackItem.PublishedAtUtc;

            try
            {
                var entity = CatalogVideo.Create(
                    youTubeVideoId: fallbackItem.YouTubeVideoId,
                    whitelistedChannelId: null,
                    channelExternalId: channelExternalId,
                    channelName: channelName,
                    title: title,
                    description: description,
                    thumbnailUrl: thumbnailUrl,
                    language: language,
                    durationSeconds: durationSeconds,
                    viewCount: viewCount,
                    likeCount: likeCount,
                    publishedAtUtc: publishedAtUtc,
                    discoverySource: DiscoverySource.YouTubeApiFallback,
                    utcNow: now);

                results.Add(entity);
            }
            catch (InvalidVideoCatalogDomainException ex)
            {
                logger.LogWarning(
                    "Skipping invalid fallback video during write-through mapping. VideoId={VideoId} ErrorCode={ErrorCode}",
                    fallbackItem.YouTubeVideoId,
                    ex.ErrorCode);
            }
        }

        return results;
    }

    private async Task PersistWriteThroughAsync(
        IReadOnlyList<CatalogVideo> fallbackCatalogVideos,
        CancellationToken cancellationToken)
    {
        try
        {
            await videoCatalogRepository.UpsertRangeAsync(fallbackCatalogVideos, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                "Write-through catalog upsert failed; returning unpersisted fallback candidates. VideoCount={VideoCount} ExceptionType={ExceptionType}",
                fallbackCatalogVideos.Count,
                ex.GetType().Name);
        }
    }

    private static IReadOnlyList<VideoDiscoveryQueryInput> SelectFallbackQueries(
        TargetFeedLanguage targetLanguage,
        IReadOnlyList<VideoDiscoveryQueryInput> enQueries,
        IReadOnlyList<VideoDiscoveryQueryInput> arQueries,
        int maxQueries)
    {
        if (targetLanguage == TargetFeedLanguage.En)
        {
            return enQueries.Take(maxQueries).ToArray();
        }

        if (targetLanguage == TargetFeedLanguage.Ar)
        {
            return arQueries.Take(maxQueries).ToArray();
        }

        return Interleave(enQueries, arQueries).Take(maxQueries).ToArray();
    }

    private static IReadOnlyList<CatalogVideo> DeduplicateCatalogVideos(IReadOnlyList<CatalogVideo> videos) =>
        DeduplicateItems(videos, v => v.YouTubeVideoId);

    private static IReadOnlyList<T> DeduplicateItems<T>(
        IEnumerable<T> items,
        Func<T, string> keySelector)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<T>();

        foreach (var item in items)
        {
            var key = keySelector(item)?.Trim();
            if (!string.IsNullOrEmpty(key) && seen.Add(key))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private static IReadOnlyList<T> InterleaveAndDeduplicate<T>(
        IReadOnlyList<T> first,
        IReadOnlyList<T> second,
        Func<T, string> keySelector) =>
        DeduplicateItems(Interleave(first, second), keySelector);

    private static IEnumerable<T> Interleave<T>(IReadOnlyList<T> first, IReadOnlyList<T> second)
    {
        var maxCount = Math.Max(first.Count, second.Count);
        for (var i = 0; i < maxCount; i++)
        {
            if (i < first.Count)
            {
                yield return first[i];
            }

            if (i < second.Count)
            {
                yield return second[i];
            }
        }
    }

    private static bool MatchesTargetFeedLanguage(string normalizedLang, TargetFeedLanguage targetLanguage) =>
        targetLanguage switch
        {
            TargetFeedLanguage.En => normalizedLang == SourceSearchQuery.EnglishCode,
            TargetFeedLanguage.Ar => normalizedLang == SourceSearchQuery.ArabicCode,
            TargetFeedLanguage.Both => true,
            _ => false
        };

    private static bool IsTerminalFallbackErrorCode(string errorCode) =>
        errorCode is "YOUTUBE_API_KEY_MISSING"
            or "YOUTUBE_API_QUOTA_EXCEEDED"
            or "YOUTUBE_API_AUTH_FAILED"
            or "YOUTUBE_API_RATE_LIMITED";

    private static int ResolveMaxDurationSeconds(int configuredMaxDurationSeconds) =>
        configuredMaxDurationSeconds > 0 ? configuredMaxDurationSeconds : 240;

    private static bool IsWithinMaxDuration(CatalogVideo video, int maxDurationSeconds) =>
        !video.DurationSeconds.HasValue || video.DurationSeconds.Value <= maxDurationSeconds;

    private static string NormalizeWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var parts = value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }
}
