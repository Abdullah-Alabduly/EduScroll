using EduScroll.Application.Exceptions;
using EduScroll.Application.VideoDiscovery.Contracts;
using EduScroll.Application.VideoDiscovery.Models;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Domain.Entities;
using EduScroll.Domain.Exceptions;
using Microsoft.Extensions.Logging;

namespace EduScroll.Application.VideoDiscovery.Services;

/// <summary>
/// Synchronizes active whitelisted educational YouTube channels into the local catalog
/// by fetching RSS feeds, optionally enriching metadata in bounded batches, and upserting
/// validated <see cref="CatalogVideo"/> records.
/// </summary>
public sealed class WhitelistCatalogSyncService(
    IWhitelistedChannelRepository channelRepository,
    IVideoCatalogRepository videoCatalogRepository,
    IYouTubeRssFeedClient rssFeedClient,
    IYouTubeVideoMetadataClient metadataClient,
    VideoDiscoveryLimits limits,
    TimeProvider timeProvider,
    ILogger<WhitelistCatalogSyncService> logger) : IWhitelistCatalogSyncService
{
    private const int MaxBatchSize = 50;
    private const int MaxVideosPerChannel = 50;

    public Task<WhitelistSyncSummaryDto> SyncActiveChannelsAsync(CancellationToken cancellationToken)
        => SyncActiveChannelsAsync(language: null, cancellationToken);

    public async Task<WhitelistSyncSummaryDto> SyncActiveChannelsAsync(
        string? language,
        CancellationToken cancellationToken)
    {
        var normalizedLanguage = NormalizeLanguageFilter(language);
        var activeChannels = await channelRepository.GetActiveAsync(normalizedLanguage, cancellationToken);

        if (activeChannels.Count == 0)
        {
            return new WhitelistSyncSummaryDto(
                ActiveChannelsCount: 0,
                SucceededChannelsCount: 0,
                FailedChannelsCount: 0,
                FetchedRssEntriesCount: 0,
                DeduplicatedVideosCount: 0,
                EnrichedVideosCount: 0,
                UpsertedVideosCount: 0,
                CompletedAtUtc: timeProvider.GetUtcNow());
        }

        var perChannelCap = Math.Clamp(limits.MaxVideosPerChannelPerSync, 1, MaxVideosPerChannel);
        var batchSize = Math.Clamp(limits.MetadataBatchSize, 1, MaxBatchSize);

        var succeededChannels = new List<WhitelistedChannel>(activeChannels.Count);
        var failedChannelsCount = 0;
        var fetchedRssEntriesCount = 0;
        var dedupedCandidates = new Dictionary<string, (WhitelistedChannel Channel, YouTubeRssVideoItem RssItem)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var channel in activeChannels)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var entries = await rssFeedClient.FetchChannelVideosAsync(
                    channel.YouTubeChannelId,
                    cancellationToken) ?? [];

                var boundedEntries = entries
                    .Where(e => e is not null)
                    .OrderByDescending(e => e.PublishedAtUtc)
                    .Take(perChannelCap)
                    .ToArray();

                fetchedRssEntriesCount += boundedEntries.Length;
                succeededChannels.Add(channel);

                foreach (var entry in boundedEntries)
                {
                    if (string.IsNullOrWhiteSpace(entry.YouTubeVideoId))
                    {
                        continue;
                    }

                    var normalizedVideoId = entry.YouTubeVideoId.Trim();
                    dedupedCandidates.TryAdd(normalizedVideoId, (channel, entry));
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failedChannelsCount++;
                var errorCode = ex is AppException appEx ? appEx.ErrorCode : "UNEXPECTED_ERROR";
                logger.LogWarning(
                    "Failed to fetch RSS feed for whitelisted channel. ChannelId={ChannelId} ExternalChannelId={ExternalChannelId} ExceptionType={ExceptionType} ErrorCode={ErrorCode}",
                    channel.Id,
                    channel.YouTubeChannelId,
                    ex.GetType().Name,
                    errorCode);
            }
        }

        var metadataByVideoId = new Dictionary<string, YouTubeVideoMetadataItem>(StringComparer.OrdinalIgnoreCase);

        if (dedupedCandidates.Count > 0)
        {
            foreach (var batch in dedupedCandidates.Keys.Chunk(batchSize))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var batchMetadata = await metadataClient.GetVideoMetadataAsync(batch, cancellationToken);
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
                    var errorCode = ex is AppException appEx ? appEx.ErrorCode : "UNEXPECTED_ERROR";
                    logger.LogWarning(
                        "YouTube video metadata enrichment failed for batch; falling back to RSS metadata. BatchSize={BatchSize} ExceptionType={ExceptionType} ErrorCode={ErrorCode}",
                        batch.Length,
                        ex.GetType().Name,
                        errorCode);
                }
            }
        }

        var catalogTimestamp = timeProvider.GetUtcNow();
        var catalogVideos = new List<CatalogVideo>(dedupedCandidates.Count);
        var enrichedVideosCount = 0;

        foreach (var (videoId, (channel, rss)) in dedupedCandidates)
        {
            var hasMetadata = metadataByVideoId.TryGetValue(videoId, out var meta);
            if (hasMetadata)
            {
                enrichedVideosCount++;
            }

            var title = ResolveRequiredText(meta?.Title, rss.Title, null, CatalogVideo.MaxTitleLength);
            var channelName = ResolveRequiredText(
                meta?.ChannelName,
                rss.ChannelName,
                channel.Name,
                CatalogVideo.MaxChannelNameLength);
            var channelExternalId = ResolveRequiredText(
                meta?.ChannelExternalId,
                rss.ChannelExternalId,
                channel.YouTubeChannelId,
                CatalogVideo.MaxChannelExternalIdLength);
            var description = !string.IsNullOrWhiteSpace(meta?.Description)
                ? meta.Description
                : rss.Description;
            var thumbnailUrl = ResolveThumbnailUrl(meta?.ThumbnailUrl, rss.ThumbnailUrl);
            var resolvedLanguage = ResolveVideoLanguage(meta?.DefaultLanguage, channel.Language);
            var durationSeconds = meta?.DurationSeconds is >= 0 ? meta.DurationSeconds : null;
            var viewCount = Math.Max(0L, Math.Max(meta?.ViewCount ?? 0L, rss.ViewCount));
            var likeCount = Math.Max(0L, meta?.LikeCount ?? 0L);
            var publishedAtUtc = meta?.PublishedAtUtc is { } metaPublished && metaPublished != default
                ? metaPublished
                : rss.PublishedAtUtc;

            try
            {
                var catalogVideo = CatalogVideo.Create(
                    youTubeVideoId: videoId,
                    whitelistedChannelId: channel.Id,
                    channelExternalId: channelExternalId,
                    channelName: channelName,
                    title: title,
                    description: description,
                    thumbnailUrl: thumbnailUrl,
                    language: resolvedLanguage,
                    durationSeconds: durationSeconds,
                    viewCount: viewCount,
                    likeCount: likeCount,
                    publishedAtUtc: publishedAtUtc,
                    discoverySource: DiscoverySource.WhitelistRss,
                    utcNow: catalogTimestamp);

                catalogVideos.Add(catalogVideo);
            }
            catch (InvalidVideoCatalogDomainException ex)
            {
                logger.LogWarning(
                    "Skipping invalid catalog video entry during whitelist sync. VideoId={VideoId} ChannelId={ChannelId} ErrorCode={ErrorCode}",
                    videoId,
                    channel.Id,
                    ex.ErrorCode);
            }
        }

        var upsertedVideosCount = catalogVideos.Count > 0
            ? await videoCatalogRepository.UpsertRangeAsync(catalogVideos, cancellationToken)
            : 0;

        var syncTime = timeProvider.GetUtcNow();
        foreach (var channel in succeededChannels)
        {
            await channelRepository.MarkSyncedAsync(channel.Id, syncTime, cancellationToken);
        }

        logger.LogInformation(
            "Whitelist catalog sync completed. Language={Language} ActiveChannels={ActiveChannels} SucceededChannels={SucceededChannels} FailedChannels={FailedChannels} FetchedRssEntries={FetchedRssEntries} DeduplicatedVideos={DeduplicatedVideos} EnrichedVideos={EnrichedVideos} UpsertedVideos={UpsertedVideos}",
            normalizedLanguage ?? "all",
            activeChannels.Count,
            succeededChannels.Count,
            failedChannelsCount,
            fetchedRssEntriesCount,
            dedupedCandidates.Count,
            enrichedVideosCount,
            upsertedVideosCount);

        return new WhitelistSyncSummaryDto(
            ActiveChannelsCount: activeChannels.Count,
            SucceededChannelsCount: succeededChannels.Count,
            FailedChannelsCount: failedChannelsCount,
            FetchedRssEntriesCount: fetchedRssEntriesCount,
            DeduplicatedVideosCount: dedupedCandidates.Count,
            EnrichedVideosCount: enrichedVideosCount,
            UpsertedVideosCount: upsertedVideosCount,
            CompletedAtUtc: syncTime);
    }

    private static string? NormalizeLanguageFilter(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return null;
        }

        var normalized = language.Trim().ToLowerInvariant();
        if (normalized is SourceSearchQuery.EnglishCode or SourceSearchQuery.ArabicCode)
        {
            return normalized;
        }

        throw new ValidationAppException(
            "Video language filter must be 'en' or 'ar'.",
            new Dictionary<string, string[]>
            {
                ["language"] = ["Allowed values are 'en' and 'ar'."]
            },
            "VIDEO_LANGUAGE_INVALID");
    }

    private static string ResolveRequiredText(
        string? preferred,
        string? fallback,
        string? secondaryFallback,
        int maxLength)
    {
        var candidate = !string.IsNullOrWhiteSpace(preferred)
            ? preferred
            : !string.IsNullOrWhiteSpace(fallback)
                ? fallback
                : secondaryFallback;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var trimmed = candidate.Trim();
        return TruncateSurrogateSafe(trimmed, maxLength);
    }

    private static string TruncateSurrogateSafe(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var length = char.IsHighSurrogate(value[maxLength - 1])
            ? maxLength - 1
            : maxLength;

        return value[..length].TrimEnd();
    }

    private static string? ResolveThumbnailUrl(string? preferredUrl, string? fallbackUrl)
    {
        if (TryNormalizeThumbnailUrl(preferredUrl, out var validPreferred))
        {
            return validPreferred;
        }

        if (TryNormalizeThumbnailUrl(fallbackUrl, out var validFallback))
        {
            return validFallback;
        }

        return null;
    }

    private static bool TryNormalizeThumbnailUrl(string? rawUrl, out string? normalizedUrl)
    {
        if (!string.IsNullOrWhiteSpace(rawUrl))
        {
            var trimmed = rawUrl.Trim();
            if (trimmed.Length <= CatalogVideo.MaxThumbnailUrlLength &&
                Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                !string.IsNullOrWhiteSpace(uri.Host))
            {
                normalizedUrl = trimmed;
                return true;
            }
        }

        normalizedUrl = null;
        return false;
    }

    private static string ResolveVideoLanguage(string? defaultLanguage, string channelLanguage)
    {
        if (!string.IsNullOrWhiteSpace(defaultLanguage))
        {
            var trimmed = defaultLanguage.Trim();
            if (trimmed.StartsWith(SourceSearchQuery.EnglishCode, StringComparison.OrdinalIgnoreCase))
            {
                return SourceSearchQuery.EnglishCode;
            }

            if (trimmed.StartsWith(SourceSearchQuery.ArabicCode, StringComparison.OrdinalIgnoreCase))
            {
                return SourceSearchQuery.ArabicCode;
            }
        }

        return channelLanguage;
    }
}
