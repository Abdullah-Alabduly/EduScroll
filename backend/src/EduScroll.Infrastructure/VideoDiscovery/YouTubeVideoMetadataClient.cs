using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
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
public sealed partial class YouTubeVideoMetadataClient(
    HttpClient httpClient,
    IOptions<YouTubeOptions> options,
    ILogger<YouTubeVideoMetadataClient> logger) : IYouTubeVideoMetadataClient
{
    private static readonly string[] ThumbnailPriorityKeys =
    [
        "high",
        "medium",
        "standard",
        "maxres",
        "default"
    ];

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
                using var httpRequest = BuildHttpRequest(endpoint);
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
                    var forbiddenKind = ClassifyForbiddenError(forbiddenBody);
                    if (forbiddenKind == ForbiddenErrorKind.RateLimited && attempt < maxAttempts)
                    {
                        await YouTubeHttpRetryHelper.DelayBackoffAsync(
                            config.RetryBaseDelayMilliseconds,
                            attempt,
                            cancellationToken);
                        continue;
                    }

                    throw MapForbiddenError(forbiddenKind);
                }

                if (YouTubeHttpRetryHelper.IsTransientStatus(statusCode) && attempt < maxAttempts)
                {
                    await YouTubeHttpRetryHelper.DelayBackoffAsync(
                        config.RetryBaseDelayMilliseconds,
                        attempt,
                        cancellationToken);
                    continue;
                }

                throw MapHttpStatusError(statusCode);
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
                var sanitizedError = RedactSensitiveText(ex.Message, apiKey);
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
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            throw new ExternalDependencyAppException(
                "YouTube Data API base URL is not configured.",
                "YOUTUBE_API_CONFIGURATION_INVALID");
        }

        var normalizedBase = apiBaseUrl.Trim().TrimEnd('/');
        var escapedIds = string.Join(',', batch.Select(Uri.EscapeDataString));
        var escapedKey = Uri.EscapeDataString(apiKey);
        var endpointString = $"{normalizedBase}/videos?part=snippet,contentDetails,statistics&id={escapedIds}&key={escapedKey}";

        if (!Uri.TryCreate(endpointString, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(endpoint.Host))
        {
            throw new ExternalDependencyAppException(
                "YouTube Data API base URL is invalid.",
                "YOUTUBE_API_CONFIGURATION_INVALID");
        }

        return endpoint;
    }

    private static HttpRequestMessage BuildHttpRequest(Uri endpoint)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
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
        var videoId = GetOptionalTrimmedString(itemElement, "id");
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

        if (TryGetObject(itemElement, "snippet", out var snippet))
        {
            channelExternalId = GetOptionalTrimmedString(snippet, "channelId");
            channelName = GetOptionalTrimmedString(snippet, "channelTitle");
            title = GetOptionalTrimmedString(snippet, "title");
            description = GetOptionalTrimmedString(snippet, "description");
            thumbnailUrl = ExtractBestThumbnailUrl(snippet);
            defaultLanguage = GetOptionalTrimmedString(snippet, "defaultAudioLanguage")
                ?? GetOptionalTrimmedString(snippet, "defaultLanguage");
            publishedAtUtc = TryGetDateTimeOffset(snippet, "publishedAt");
        }

        int? durationSeconds = null;
        if (TryGetObject(itemElement, "contentDetails", out var contentDetails))
        {
            durationSeconds = ParseIso8601DurationSeconds(GetOptionalTrimmedString(contentDetails, "duration"));
        }

        long? viewCount = null;
        long? likeCount = null;
        if (TryGetObject(itemElement, "statistics", out var statistics))
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

    private static string? ExtractBestThumbnailUrl(JsonElement snippet)
    {
        if (!TryGetObject(snippet, "thumbnails", out var thumbnails))
        {
            return null;
        }

        foreach (var key in ThumbnailPriorityKeys)
        {
            if (TryGetObject(thumbnails, key, out var thumbnail) &&
                GetOptionalTrimmedString(thumbnail, "url") is { } url)
            {
                return url;
            }
        }

        return null;
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

    private static DateTimeOffset? TryGetDateTimeOffset(JsonElement element, string propertyName)
    {
        var raw = GetOptionalTrimmedString(element, propertyName);
        if (raw is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static bool TryGetObject(JsonElement element, string propertyName, out JsonElement obj)
    {
        if (element.TryGetProperty(propertyName, out obj) &&
            obj.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        obj = default;
        return false;
    }

    private static string? GetOptionalTrimmedString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop) ||
            prop.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = prop.GetString();
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    private static ForbiddenErrorKind ClassifyForbiddenError(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return ForbiddenErrorKind.AuthFailed;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !TryGetObject(doc.RootElement, "error", out var errorEl))
            {
                return ForbiddenErrorKind.AuthFailed;
            }

            var hasRateLimitReason = false;

            if (errorEl.TryGetProperty("errors", out var errorsEl) &&
                errorsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in errorsEl.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var reason = GetOptionalTrimmedString(entry, "reason");
                    if (IsQuotaReason(reason))
                    {
                        return ForbiddenErrorKind.QuotaExceeded;
                    }

                    if (IsRateLimitReason(reason))
                    {
                        hasRateLimitReason = true;
                    }

                    var entryMessage = GetOptionalTrimmedString(entry, "message");
                    if (ContainsQuotaKeyword(entryMessage))
                    {
                        return ForbiddenErrorKind.QuotaExceeded;
                    }
                }
            }

            var errorMessage = GetOptionalTrimmedString(errorEl, "message");
            if (ContainsQuotaKeyword(errorMessage))
            {
                return ForbiddenErrorKind.QuotaExceeded;
            }

            return hasRateLimitReason
                ? ForbiddenErrorKind.RateLimited
                : ForbiddenErrorKind.AuthFailed;
        }
        catch (JsonException)
        {
            return ForbiddenErrorKind.AuthFailed;
        }
    }

    private static bool IsQuotaReason(string? reason) =>
        string.Equals(reason, "quotaExceeded", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(reason, "dailyLimitExceeded", StringComparison.OrdinalIgnoreCase);

    private static bool IsRateLimitReason(string? reason) =>
        string.Equals(reason, "rateLimitExceeded", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(reason, "userRateLimitExceeded", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsQuotaKeyword(string? message) =>
        !string.IsNullOrWhiteSpace(message) &&
        message.Contains("quota", StringComparison.OrdinalIgnoreCase);

    private static AppException MapForbiddenError(ForbiddenErrorKind kind) =>
        kind switch
        {
            ForbiddenErrorKind.QuotaExceeded => new ExternalDependencyAppException(
                "YouTube Data API quota has been exceeded.",
                "YOUTUBE_API_QUOTA_EXCEEDED"),
            ForbiddenErrorKind.RateLimited => new ExternalDependencyAppException(
                "YouTube Data API is currently rate-limited. Please try again shortly.",
                "YOUTUBE_API_RATE_LIMITED"),
            _ => new ExternalDependencyAppException(
                "YouTube Data API authentication or authorization failed.",
                "YOUTUBE_API_AUTH_FAILED")
        };

    private static AppException MapHttpStatusError(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new ExternalDependencyAppException(
                    "YouTube Data API authentication or authorization failed.",
                    "YOUTUBE_API_AUTH_FAILED"),
            HttpStatusCode.TooManyRequests =>
                new ExternalDependencyAppException(
                    "YouTube Data API is currently rate-limited. Please try again shortly.",
                    "YOUTUBE_API_RATE_LIMITED"),
            HttpStatusCode.RequestTimeout =>
                new GatewayTimeoutAppException(
                    "YouTube Data API request timed out. Please try again later.",
                    "YOUTUBE_API_TIMEOUT"),
            _ => new ExternalDependencyAppException(
                "YouTube Data API is currently unavailable. Please try again later.",
                "YOUTUBE_API_UNAVAILABLE")
        };

    private static string RedactSensitiveText(string? text, string apiKey)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sanitized = text;
        if (!string.IsNullOrEmpty(apiKey))
        {
            sanitized = sanitized.Replace(apiKey, "[REDACTED]", StringComparison.Ordinal);

            var escapedKey = Uri.EscapeDataString(apiKey);
            if (!string.IsNullOrEmpty(escapedKey))
            {
                sanitized = sanitized.Replace(escapedKey, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
            }
        }

        return KeyQueryParameterRegex().Replace(sanitized, "$1[REDACTED]");
    }

    [GeneratedRegex(@"([?&]key=)[^&\s""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyQueryParameterRegex();

    private enum ForbiddenErrorKind
    {
        AuthFailed,
        QuotaExceeded,
        RateLimited
    }
}
