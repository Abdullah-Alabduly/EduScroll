using System.Diagnostics;
using System.Net;
using System.Text.Json;
using EduScroll.Application.Exceptions;
using EduScroll.Application.VideoDiscovery.Models;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Domain.Entities;
using EduScroll.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduScroll.Infrastructure.VideoDiscovery;

/// <summary>
/// HTTP adapter for YouTube Data API v3 <c>/search</c> fallback discovery.
/// Enforces <c>type=video</c>, <c>videoEmbeddable=true</c>, <c>safeSearch=strict</c>,
/// <c>relevanceLanguage</c>, bounded timeouts/retries, caller cancellation propagation,
/// and strict API key redaction across logs and exceptions.
/// </summary>
public sealed class YouTubeSearchFallbackClient(
    HttpClient httpClient,
    IOptions<YouTubeOptions> options,
    ILogger<YouTubeSearchFallbackClient> logger) : IYouTubeSearchFallbackClient
{
    public async Task<IReadOnlyList<YouTubeSearchFallbackItem>> SearchVideosAsync(
        string query,
        string relevanceLanguage,
        int maxResults,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(relevanceLanguage);

        var normalizedLanguage = relevanceLanguage.Trim().ToLowerInvariant();
        if (normalizedLanguage is not (SourceSearchQuery.EnglishCode or SourceSearchQuery.ArabicCode))
        {
            throw new ArgumentException("Relevance language must be 'en' or 'ar'.", nameof(relevanceLanguage));
        }

        if (maxResults <= 0)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();

        var config = options.Value;
        if (string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new ExternalDependencyAppException(
                "YouTube Data API key is not configured.",
                "YOUTUBE_API_KEY_MISSING");
        }

        var apiKey = config.ApiKey.Trim();
        var trimmedQuery = query.Trim();
        var boundedMaxResults = Math.Clamp(maxResults, 1, YouTubeOptions.MaxAllowedFallbackResultsPerQuery);
        var endpoint = BuildSearchEndpoint(
            config.ApiBaseUrl,
            trimmedQuery,
            normalizedLanguage,
            boundedMaxResults,
            config.SearchVideoDuration,
            apiKey);

        var maxAttempts = 1 + Math.Max(0, config.MaxRetries);
        var timeout = TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds, 1, 120));

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stopwatch = Stopwatch.StartNew();
            using var attemptTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeoutCts.CancelAfter(timeout);

            try
            {
                using var httpRequest = YouTubeDataApiHelper.BuildHttpRequest(endpoint);
                using var response = await httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseHeadersRead,
                    attemptTimeoutCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync(attemptTimeoutCts.Token);
                    stopwatch.Stop();

                    var parsedItems = ParseSearchResponse(responseBody, normalizedLanguage, attempt);

                    logger.LogInformation(
                        "YouTube search fallback succeeded. RelevanceLanguage={RelevanceLanguage} MaxResults={MaxResults} ReturnedItems={ReturnedItems} Attempt={Attempt} DurationMs={DurationMs}",
                        normalizedLanguage,
                        boundedMaxResults,
                        parsedItems.Count,
                        attempt,
                        stopwatch.ElapsedMilliseconds);

                    return parsedItems;
                }

                var statusCode = response.StatusCode;
                string? forbiddenBody = null;
                if (statusCode == HttpStatusCode.Forbidden)
                {
                    forbiddenBody = await response.Content.ReadAsStringAsync(attemptTimeoutCts.Token);
                }

                stopwatch.Stop();

                logger.LogWarning(
                    "YouTube Data API search returned non-success status. StatusCode={StatusCode} RelevanceLanguage={RelevanceLanguage} Attempt={Attempt}/{MaxAttempts} DurationMs={DurationMs}",
                    (int)statusCode,
                    normalizedLanguage,
                    attempt,
                    maxAttempts,
                    stopwatch.ElapsedMilliseconds);

                if (statusCode == HttpStatusCode.Forbidden)
                {
                    var forbiddenKind = YouTubeDataApiHelper.ClassifyForbiddenError(forbiddenBody);
                    if (forbiddenKind == YouTubeDataApiHelper.ForbiddenErrorKind.RateLimited && attempt < maxAttempts)
                    {
                        await YouTubeHttpRetryHelper.DelayBackoffAsync(
                            config.RetryBaseDelayMilliseconds,
                            attempt,
                            cancellationToken);
                        continue;
                    }

                    throw YouTubeDataApiHelper.MapForbiddenError(forbiddenKind);
                }

                if (YouTubeHttpRetryHelper.IsTransientStatus(statusCode) && attempt < maxAttempts)
                {
                    await YouTubeHttpRetryHelper.DelayBackoffAsync(
                        config.RetryBaseDelayMilliseconds,
                        attempt,
                        cancellationToken);
                    continue;
                }

                throw YouTubeDataApiHelper.MapHttpStatusError(statusCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                logger.LogWarning(
                    "YouTube Data API search request timed out. RelevanceLanguage={RelevanceLanguage} Attempt={Attempt}/{MaxAttempts} TimeoutSeconds={TimeoutSeconds}",
                    normalizedLanguage,
                    attempt,
                    maxAttempts,
                    config.TimeoutSeconds);

                if (attempt < maxAttempts)
                {
                    await YouTubeHttpRetryHelper.DelayBackoffAsync(
                        config.RetryBaseDelayMilliseconds,
                        attempt,
                        cancellationToken);
                    continue;
                }

                throw new GatewayTimeoutAppException(
                    "YouTube Data API search request timed out. Please try again later.",
                    "YOUTUBE_API_TIMEOUT");
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                var sanitizedError = YouTubeDataApiHelper.RedactSensitiveText(ex.Message, apiKey);
                logger.LogWarning(
                    "YouTube Data API search transport error. RelevanceLanguage={RelevanceLanguage} Attempt={Attempt}/{MaxAttempts} Error={Error}",
                    normalizedLanguage,
                    attempt,
                    maxAttempts,
                    sanitizedError);

                if (attempt < maxAttempts)
                {
                    await YouTubeHttpRetryHelper.DelayBackoffAsync(
                        config.RetryBaseDelayMilliseconds,
                        attempt,
                        cancellationToken);
                    continue;
                }

                throw new ExternalDependencyAppException(
                    "YouTube Data API is currently unreachable. Please try again later.",
                    "YOUTUBE_API_UNAVAILABLE");
            }
        }

        throw new ExternalDependencyAppException(
            "YouTube Data API is currently unavailable.",
            "YOUTUBE_API_UNAVAILABLE");
    }

    private static Uri BuildSearchEndpoint(
        string apiBaseUrl,
        string query,
        string relevanceLanguage,
        int maxResults,
        string? searchVideoDuration,
        string apiKey)
    {
        var escapedLang = Uri.EscapeDataString(relevanceLanguage);
        var escapedQuery = Uri.EscapeDataString(query);
        var escapedKey = Uri.EscapeDataString(apiKey);
        var normalizedDuration = searchVideoDuration?.Trim().ToLowerInvariant();
        var durationParam = normalizedDuration is "short" or "medium" or "long"
            ? $"&videoDuration={Uri.EscapeDataString(normalizedDuration)}"
            : string.Empty;

        var relative =
            $"search?part=snippet&type=video&videoEmbeddable=true&safeSearch=strict&relevanceLanguage={escapedLang}{durationParam}&q={escapedQuery}&maxResults={maxResults}&key={escapedKey}";

        return YouTubeDataApiHelper.BuildEndpointUri(apiBaseUrl, relative);
    }

    private IReadOnlyList<YouTubeSearchFallbackItem> ParseSearchResponse(
        string responseBody,
        string relevanceLanguage,
        int attempt)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            logger.LogWarning(
                "YouTube Data API search returned an empty response body. RelevanceLanguage={RelevanceLanguage} Attempt={Attempt}",
                relevanceLanguage,
                attempt);
            throw new ExternalDependencyAppException(
                "YouTube Data API returned an empty search response.",
                "YOUTUBE_API_INVALID_RESPONSE");
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new ExternalDependencyAppException(
                    "YouTube Data API returned an unexpected search response envelope.",
                    "YOUTUBE_API_INVALID_RESPONSE");
            }

            if (!doc.RootElement.TryGetProperty("items", out var itemsElement))
            {
                return [];
            }

            if (itemsElement.ValueKind != JsonValueKind.Array)
            {
                throw new ExternalDependencyAppException(
                    "YouTube Data API returned an unexpected search items payload.",
                    "YOUTUBE_API_INVALID_RESPONSE");
            }

            var items = new List<YouTubeSearchFallbackItem>();
            var seenVideoIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var itemElement in itemsElement.EnumerateArray())
            {
                if (itemElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var parsed = ParseSearchItem(itemElement, relevanceLanguage);
                if (parsed is not null && seenVideoIds.Add(parsed.YouTubeVideoId))
                {
                    items.Add(parsed);
                }
            }

            return items;
        }
        catch (JsonException)
        {
            logger.LogWarning(
                "YouTube Data API search returned malformed JSON. RelevanceLanguage={RelevanceLanguage} Attempt={Attempt}",
                relevanceLanguage,
                attempt);
            throw new ExternalDependencyAppException(
                "YouTube Data API returned a malformed JSON search response.",
                "YOUTUBE_API_INVALID_RESPONSE");
        }
    }

    private static YouTubeSearchFallbackItem? ParseSearchItem(
        JsonElement itemElement,
        string relevanceLanguage)
    {
        if (!YouTubeDataApiHelper.TryGetObject(itemElement, "id", out var idObject))
        {
            return null;
        }

        var videoId = YouTubeDataApiHelper.GetOptionalTrimmedString(idObject, "videoId");
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return null;
        }

        if (!YouTubeDataApiHelper.TryGetObject(itemElement, "snippet", out var snippet))
        {
            return null;
        }

        var channelExternalId = YouTubeDataApiHelper.GetOptionalTrimmedString(snippet, "channelId");
        var rawTitle = YouTubeDataApiHelper.GetOptionalTrimmedString(snippet, "title");
        if (string.IsNullOrWhiteSpace(channelExternalId) || string.IsNullOrWhiteSpace(rawTitle))
        {
            return null;
        }

        var rawChannelTitle = YouTubeDataApiHelper.GetOptionalTrimmedString(snippet, "channelTitle");
        var rawDescription = YouTubeDataApiHelper.GetOptionalTrimmedString(snippet, "description");
        var thumbnailUrl = YouTubeDataApiHelper.ExtractBestThumbnailUrl(snippet);
        var publishedAtUtc = YouTubeDataApiHelper.TryGetDateTimeOffset(snippet, "publishedAt")
            ?? DateTimeOffset.UnixEpoch;

        var title = WebUtility.HtmlDecode(rawTitle).Trim();
        var channelName = string.IsNullOrWhiteSpace(rawChannelTitle)
            ? channelExternalId
            : WebUtility.HtmlDecode(rawChannelTitle).Trim();
        var description = string.IsNullOrWhiteSpace(rawDescription)
            ? string.Empty
            : WebUtility.HtmlDecode(rawDescription).Trim();

        return new YouTubeSearchFallbackItem(
            YouTubeVideoId: videoId,
            ChannelExternalId: channelExternalId,
            ChannelName: channelName,
            Title: title,
            Description: description,
            ThumbnailUrl: thumbnailUrl,
            Language: relevanceLanguage,
            PublishedAtUtc: publishedAtUtc);
    }
}
