using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Xml;
using EduScroll.Application.Exceptions;
using EduScroll.Application.VideoDiscovery.Models;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduScroll.Infrastructure.VideoDiscovery;

/// <summary>
/// HTTP adapter for YouTube Data API v3 <c>/videos</c> metadata enrichment.
/// Enforces batch chunking (max 50 IDs/request), per-attempt timeouts, bounded exponential backoff,
/// immediate caller cancellation propagation, and strict API key redaction across logs and exceptions.
/// </summary>
public sealed class YouTubeVideoMetadataClient(
    HttpClient httpClient,
    IOptions<YouTubeOptions> options,
    ILogger<YouTubeVideoMetadataClient> logger) : IYouTubeVideoMetadataClient
{
    public async Task<IReadOnlyDictionary<string, YouTubeVideoMetadataItem>> GetVideoMetadataAsync(
        IReadOnlyCollection<string> videoIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(videoIds);

        var uniqueIds = videoIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (uniqueIds.Length == 0)
        {
            return new Dictionary<string, YouTubeVideoMetadataItem>(StringComparer.OrdinalIgnoreCase);
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
        var batchSize = Math.Clamp(config.MetadataBatchSize, 1, YouTubeOptions.MaxAllowedMetadataBatchSize);
        var results = new Dictionary<string, YouTubeVideoMetadataItem>(
            uniqueIds.Length,
            StringComparer.OrdinalIgnoreCase);

        foreach (var batch in uniqueIds.Chunk(batchSize))
        {
            await FetchBatchAsync(batch, config, apiKey, results, cancellationToken);
        }

        return results;
    }

    private async Task FetchBatchAsync(
        string[] batch,
        YouTubeOptions config,
        string apiKey,
        IDictionary<string, YouTubeVideoMetadataItem> results,
        CancellationToken cancellationToken)
    {
        var endpoint = BuildVideosEndpoint(config.ApiBaseUrl, batch, apiKey);
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

                    var parsedCount = ParseBatchResponse(responseBody, results, batch.Length, attempt);

                    logger.LogInformation(
                        "YouTube metadata batch succeeded. BatchSize={BatchSize} ReturnedItems={ReturnedItems} Attempt={Attempt} DurationMs={DurationMs}",
                        batch.Length,
                        parsedCount,
                        attempt,
                        stopwatch.ElapsedMilliseconds);

                    return;
                }

                var statusCode = response.StatusCode;
                string? forbiddenBody = null;
                if (statusCode == HttpStatusCode.Forbidden)
                {
                    forbiddenBody = await response.Content.ReadAsStringAsync(attemptTimeoutCts.Token);
                }

                stopwatch.Stop();

                logger.LogWarning(
                    "YouTube Data API returned non-success status. StatusCode={StatusCode} BatchSize={BatchSize} Attempt={Attempt}/{MaxAttempts} DurationMs={DurationMs}",
                    (int)statusCode,
                    batch.Length,
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
                // Propagate caller cancellation immediately without retrying.
                throw;
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                logger.LogWarning(
                    "YouTube Data API request timed out. BatchSize={BatchSize} Attempt={Attempt}/{MaxAttempts} TimeoutSeconds={TimeoutSeconds}",
                    batch.Length,
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
                    "YouTube Data API request timed out. Please try again later.",
                    "YOUTUBE_API_TIMEOUT");
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                var sanitizedError = YouTubeDataApiHelper.RedactSensitiveText(ex.Message, apiKey);
                logger.LogWarning(
                    "YouTube Data API transport error. BatchSize={BatchSize} Attempt={Attempt}/{MaxAttempts} Error={Error}",
                    batch.Length,
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
    }

    private static Uri BuildVideosEndpoint(string apiBaseUrl, string[] batch, string apiKey)
    {
        var escapedIds = string.Join(',', batch.Select(Uri.EscapeDataString));
        var escapedKey = Uri.EscapeDataString(apiKey);
        return YouTubeDataApiHelper.BuildEndpointUri(
            apiBaseUrl,
            $"videos?part=snippet,contentDetails,statistics&id={escapedIds}&key={escapedKey}");
    }

    private int ParseBatchResponse(
        string responseBody,
        IDictionary<string, YouTubeVideoMetadataItem> results,
        int batchSize,
        int attempt)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            logger.LogWarning(
                "YouTube Data API returned an empty response body. BatchSize={BatchSize} Attempt={Attempt}",
                batchSize,
                attempt);
            throw new ExternalDependencyAppException(
                "YouTube Data API returned an empty response.",
                "YOUTUBE_API_INVALID_RESPONSE");
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                logger.LogWarning(
                    "YouTube Data API returned a non-object JSON payload. BatchSize={BatchSize} Attempt={Attempt}",
                    batchSize,
                    attempt);
                throw new ExternalDependencyAppException(
                    "YouTube Data API returned an unexpected response envelope.",
                    "YOUTUBE_API_INVALID_RESPONSE");
            }

            if (!doc.RootElement.TryGetProperty("items", out var itemsElement))
            {
                return 0;
            }

            if (itemsElement.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning(
                    "YouTube Data API returned a non-array items property. BatchSize={BatchSize} Attempt={Attempt}",
                    batchSize,
                    attempt);
                throw new ExternalDependencyAppException(
                    "YouTube Data API returned an unexpected items payload.",
                    "YOUTUBE_API_INVALID_RESPONSE");
            }

            var parsedCount = 0;
            foreach (var itemElement in itemsElement.EnumerateArray())
            {
                if (itemElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var parsedItem = ParseVideoItem(itemElement);
                if (parsedItem is null)
                {
                    continue;
                }

                results[parsedItem.YouTubeVideoId] = parsedItem;
                parsedCount++;
            }

            return parsedCount;
        }
        catch (JsonException)
        {
            logger.LogWarning(
                "YouTube Data API returned malformed JSON. BatchSize={BatchSize} Attempt={Attempt}",
                batchSize,
                attempt);
            throw new ExternalDependencyAppException(
                "YouTube Data API returned a malformed JSON response.",
                "YOUTUBE_API_INVALID_RESPONSE");
        }
    }

    private static YouTubeVideoMetadataItem? ParseVideoItem(JsonElement itemElement)
    {
        var videoId = YouTubeDataApiHelper.GetOptionalTrimmedString(itemElement, "id");
        if (string.IsNullOrWhiteSpace(videoId))
        {
            return null;
        }

        string? channelExternalId = null;
        string? channelName = null;
        string? title = null;
        string? description = null;
        string? thumbnailUrl = null;
        string? defaultLanguage = null;
        DateTimeOffset? publishedAtUtc = null;

        if (YouTubeDataApiHelper.TryGetObject(itemElement, "snippet", out var snippet))
        {
            channelExternalId = YouTubeDataApiHelper.GetOptionalTrimmedString(snippet, "channelId");
            channelName = YouTubeDataApiHelper.GetOptionalTrimmedString(snippet, "channelTitle");
            title = YouTubeDataApiHelper.GetOptionalTrimmedString(snippet, "title");
            description = YouTubeDataApiHelper.GetOptionalTrimmedString(snippet, "description");
            thumbnailUrl = YouTubeDataApiHelper.ExtractBestThumbnailUrl(snippet);
            defaultLanguage = YouTubeDataApiHelper.GetOptionalTrimmedString(snippet, "defaultAudioLanguage")
                ?? YouTubeDataApiHelper.GetOptionalTrimmedString(snippet, "defaultLanguage");
            publishedAtUtc = YouTubeDataApiHelper.TryGetDateTimeOffset(snippet, "publishedAt");
        }

        int? durationSeconds = null;
        if (YouTubeDataApiHelper.TryGetObject(itemElement, "contentDetails", out var contentDetails))
        {
            durationSeconds = ParseIso8601DurationSeconds(
                YouTubeDataApiHelper.GetOptionalTrimmedString(contentDetails, "duration"));
        }

        long? viewCount = null;
        long? likeCount = null;
        if (YouTubeDataApiHelper.TryGetObject(itemElement, "statistics", out var statistics))
        {
            viewCount = ParseOptionalInt64(statistics, "viewCount");
            likeCount = ParseOptionalInt64(statistics, "likeCount");
        }

        return new YouTubeVideoMetadataItem(
            YouTubeVideoId: videoId,
            ChannelExternalId: channelExternalId,
            ChannelName: channelName,
            Title: title,
            Description: description,
            ThumbnailUrl: thumbnailUrl,
            DefaultLanguage: defaultLanguage,
            DurationSeconds: durationSeconds,
            ViewCount: viewCount,
            LikeCount: likeCount,
            PublishedAtUtc: publishedAtUtc);
    }

    private static int? ParseIso8601DurationSeconds(string? isoDuration)
    {
        if (string.IsNullOrWhiteSpace(isoDuration))
        {
            return null;
        }

        try
        {
            var duration = XmlConvert.ToTimeSpan(isoDuration.Trim());
            if (duration.TotalSeconds is < 0 or > int.MaxValue || double.IsNaN(duration.TotalSeconds))
            {
                return null;
            }

            return (int)duration.TotalSeconds;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }
    }

    private static long? ParseOptionalInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop))
        {
            return null;
        }

        if (prop.ValueKind == JsonValueKind.String)
        {
            var raw = prop.GetString();
            return long.TryParse(raw?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                   parsed >= 0
                ? parsed
                : null;
        }

        if (prop.ValueKind == JsonValueKind.Number)
        {
            return prop.TryGetInt64(out var parsed) && parsed >= 0
                ? parsed
                : null;
        }

        return null;
    }
}
