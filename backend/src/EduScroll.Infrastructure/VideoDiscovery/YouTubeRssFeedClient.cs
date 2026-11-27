using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Xml;
using System.Xml.Linq;
using EduScroll.Application.Exceptions;
using EduScroll.Application.VideoDiscovery.Models;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduScroll.Infrastructure.VideoDiscovery;

/// <summary>
/// Fetches and safely parses YouTube channel Atom RSS feeds with bounded retries,
/// per-attempt timeouts, and DTD/XXE-prohibited XML parsing.
/// </summary>
public sealed class YouTubeRssFeedClient(
    HttpClient httpClient,
    IOptions<YouTubeOptions> options,
    ILogger<YouTubeRssFeedClient> logger) : IYouTubeRssFeedClient
{
    private const int MaxXmlCharacters = 5_000_000;
    private const int MaxResponseBytes = 20 * 1024 * 1024;
    private const string VideoIdPrefix = "yt:video:";
    private const string ChannelIdPrefix = "yt:channel:";

    private static readonly XNamespace AtomNamespace = "http://www.w3.org/2005/Atom";
    private static readonly XNamespace YouTubeNamespace = "http://www.youtube.com/xml/schemas/2015";
    private static readonly XNamespace MediaNamespace = "http://search.yahoo.com/mrss/";

    private static readonly XmlReaderSettings SafeXmlReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxXmlCharacters,
        Async = true
    };

    public async Task<IReadOnlyList<YouTubeRssVideoItem>> FetchChannelVideosAsync(
        string channelId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(channelId);

        var trimmedChannelId = channelId.Trim();
        var config = options.Value;

        if (string.IsNullOrWhiteSpace(config.RssBaseUrl))
        {
            throw new ExternalDependencyAppException(
                "YouTube RSS base URL is not configured.",
                "YOUTUBE_RSS_FETCH_FAILED");
        }

        var feedUri = BuildFeedUri(config.RssBaseUrl, trimmedChannelId);
        var timeoutSeconds = Math.Max(1, config.TimeoutSeconds);
        var maxAttempts = 1 + Math.Max(0, config.MaxRetries);

        byte[]? responseBytes = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();
            using var attemptTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                using var request = CreateRequest(feedUri);
                using var response = await httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    attemptTimeoutCts.Token);

                if (response.IsSuccessStatusCode)
                {
                    responseBytes = await ReadBoundedResponseBytesAsync(
                        response.Content,
                        MaxResponseBytes,
                        attemptTimeoutCts.Token);

                    sw.Stop();
                    logger.LogInformation(
                        "YouTube RSS feed fetched successfully. ChannelId={ChannelId} Attempt={Attempt} Bytes={Bytes} DurationMs={DurationMs}",
                        trimmedChannelId,
                        attempt,
                        responseBytes.Length,
                        sw.ElapsedMilliseconds);
                    break;
                }

                sw.Stop();
                var statusCode = response.StatusCode;
                logger.LogWarning(
                    "YouTube RSS feed returned non-success HTTP status. ChannelId={ChannelId} StatusCode={StatusCode} Attempt={Attempt}/{MaxAttempts} DurationMs={DurationMs}",
                    trimmedChannelId,
                    (int)statusCode,
                    attempt,
                    maxAttempts,
                    sw.ElapsedMilliseconds);

                if (YouTubeHttpRetryHelper.IsTransientStatus(statusCode) && attempt < maxAttempts)
                {
                    await YouTubeHttpRetryHelper.DelayBackoffAsync(
                        config.RetryBaseDelayMilliseconds,
                        attempt,
                        cancellationToken);
                    continue;
                }

                throw new ExternalDependencyAppException(
                    $"Failed to fetch YouTube RSS feed (HTTP {(int)statusCode}).",
                    "YOUTUBE_RSS_FETCH_FAILED");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Propagate caller cancellation immediately without retrying.
                throw;
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                logger.LogWarning(
                    "YouTube RSS feed request timed out. ChannelId={ChannelId} Attempt={Attempt}/{MaxAttempts} TimeoutSeconds={TimeoutSeconds}",
                    trimmedChannelId,
                    attempt,
                    maxAttempts,
                    timeoutSeconds);

                if (attempt < maxAttempts)
                {
                    await YouTubeHttpRetryHelper.DelayBackoffAsync(
                        config.RetryBaseDelayMilliseconds,
                        attempt,
                        cancellationToken);
                    continue;
                }

                throw new GatewayTimeoutAppException(
                    "YouTube RSS feed request timed out.",
                    "YOUTUBE_RSS_TIMEOUT");
            }
            catch (HttpRequestException ex)
            {
                sw.Stop();
                logger.LogWarning(
                    ex,
                    "YouTube RSS feed transport error. ChannelId={ChannelId} Attempt={Attempt}/{MaxAttempts}",
                    trimmedChannelId,
                    attempt,
                    maxAttempts);

                if (attempt < maxAttempts)
                {
                    await YouTubeHttpRetryHelper.DelayBackoffAsync(
                        config.RetryBaseDelayMilliseconds,
                        attempt,
                        cancellationToken);
                    continue;
                }

                throw new ExternalDependencyAppException(
                    "Failed to fetch YouTube RSS feed due to a network error.",
                    "YOUTUBE_RSS_FETCH_FAILED");
            }
        }

        if (responseBytes is null)
        {
            throw new ExternalDependencyAppException(
                "Failed to fetch YouTube RSS feed.",
                "YOUTUBE_RSS_FETCH_FAILED");
        }

        return await ParseAtomFeedAsync(responseBytes, trimmedChannelId, cancellationToken);
    }

    private static Uri BuildFeedUri(string rssBaseUrl, string channelId)
    {
        var builder = new UriBuilder(rssBaseUrl.Trim());
        var channelParam = $"channel_id={Uri.EscapeDataString(channelId)}";
        var existingQuery = builder.Query.TrimStart('?');
        builder.Query = string.IsNullOrEmpty(existingQuery)
            ? channelParam
            : $"{existingQuery}&{channelParam}";
        return builder.Uri;
    }

    private static HttpRequestMessage CreateRequest(Uri feedUri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, feedUri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/atom+xml"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
        return request;
    }

    private static async Task<byte[]> ReadBoundedResponseBytesAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memoryStream = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var totalBytes = 0;

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalBytes += bytesRead;
            if (totalBytes > maxBytes)
            {
                throw new ExternalDependencyAppException(
                    "YouTube RSS feed response exceeded the maximum allowed size.",
                    "YOUTUBE_RSS_INVALID_XML");
            }

            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return memoryStream.ToArray();
    }

    private async Task<IReadOnlyList<YouTubeRssVideoItem>> ParseAtomFeedAsync(
        byte[] xmlBytes,
        string requestedChannelId,
        CancellationToken cancellationToken)
    {
        if (xmlBytes.Length == 0)
        {
            throw new ExternalDependencyAppException(
                "YouTube RSS feed returned an empty XML document.",
                "YOUTUBE_RSS_INVALID_XML");
        }

        try
        {
            using var stream = new MemoryStream(xmlBytes, writable: false);
            using var reader = XmlReader.Create(stream, SafeXmlReaderSettings);

            var document = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
            var root = document.Root;

            if (root is null || root.Name != AtomNamespace + "feed")
            {
                throw new ExternalDependencyAppException(
                    "YouTube RSS feed root element must be an Atom <feed>.",
                    "YOUTUBE_RSS_INVALID_XML");
            }

            var feedChannelId = ExtractFeedChannelId(root) ?? requestedChannelId;
            var feedChannelName = FirstNonWhiteSpace(
                root.Element(AtomNamespace + "author")?.Element(AtomNamespace + "name")?.Value,
                root.Element(AtomNamespace + "title")?.Value) ?? string.Empty;

            var items = new List<YouTubeRssVideoItem>();

            foreach (var entry in root.Elements(AtomNamespace + "entry"))
            {
                var videoId = ExtractVideoId(entry);
                if (string.IsNullOrWhiteSpace(videoId))
                {
                    continue;
                }

                var channelExternalId = FirstNonWhiteSpace(
                    entry.Element(YouTubeNamespace + "channelId")?.Value,
                    feedChannelId) ?? requestedChannelId;

                var channelName = FirstNonWhiteSpace(
                    entry.Element(AtomNamespace + "author")?.Element(AtomNamespace + "name")?.Value,
                    feedChannelName) ?? string.Empty;

                var mediaGroup = entry.Element(MediaNamespace + "group");

                var title = FirstNonWhiteSpace(
                    entry.Element(AtomNamespace + "title")?.Value,
                    mediaGroup?.Element(MediaNamespace + "title")?.Value) ?? string.Empty;

                var description = mediaGroup?.Element(MediaNamespace + "description")?.Value?.Trim()
                    ?? string.Empty;

                var thumbnailUrl = mediaGroup?
                    .Elements(MediaNamespace + "thumbnail")
                    .Select(el => el.Attribute("url")?.Value?.Trim())
                    .FirstOrDefault(url => !string.IsNullOrWhiteSpace(url));

                var viewCount = ParseViewCount(
                    mediaGroup?
                        .Element(MediaNamespace + "community")?
                        .Element(MediaNamespace + "statistics")?
                        .Attribute("views")?.Value);

                var publishedAtUtc = ParsePublishedTimestamp(
                    entry.Element(AtomNamespace + "published")?.Value,
                    entry.Element(AtomNamespace + "updated")?.Value);

                items.Add(new YouTubeRssVideoItem(
                    YouTubeVideoId: videoId,
                    ChannelExternalId: channelExternalId,
                    ChannelName: channelName,
                    Title: title,
                    Description: description,
                    ThumbnailUrl: thumbnailUrl,
                    ViewCount: viewCount,
                    PublishedAtUtc: publishedAtUtc));
            }

            return items;
        }
        catch (XmlException ex)
        {
            logger.LogWarning(
                "YouTube RSS feed returned invalid or prohibited XML. ChannelId={ChannelId} ExceptionType={ExceptionType}",
                requestedChannelId,
                ex.GetType().Name);

            throw new ExternalDependencyAppException(
                "YouTube RSS feed returned malformed or invalid XML.",
                "YOUTUBE_RSS_INVALID_XML");
        }
    }

    private static string? ExtractFeedChannelId(XElement feedRoot)
    {
        var ytChannelId = feedRoot.Element(YouTubeNamespace + "channelId")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(ytChannelId))
        {
            return ytChannelId;
        }

        var rawFeedId = feedRoot.Element(AtomNamespace + "id")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(rawFeedId) &&
            rawFeedId.StartsWith(ChannelIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var extracted = rawFeedId[ChannelIdPrefix.Length..].Trim();
            return string.IsNullOrWhiteSpace(extracted) ? null : extracted;
        }

        return null;
    }

    private static string? ExtractVideoId(XElement entry)
    {
        var ytVideoId = entry.Element(YouTubeNamespace + "videoId")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(ytVideoId))
        {
            return ytVideoId;
        }

        var rawEntryId = entry.Element(AtomNamespace + "id")?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(rawEntryId) &&
            rawEntryId.StartsWith(VideoIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var extracted = rawEntryId[VideoIdPrefix.Length..].Trim();
            return string.IsNullOrWhiteSpace(extracted) ? null : extracted;
        }

        return null;
    }

    private static long ParseViewCount(string? rawViews)
    {
        if (string.IsNullOrWhiteSpace(rawViews))
        {
            return 0;
        }

        return long.TryParse(
            rawViews.Trim(),
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out var views) && views >= 0
            ? views
            : 0;
    }

    private static DateTimeOffset ParsePublishedTimestamp(string? publishedRaw, string? updatedRaw)
    {
        var candidate = FirstNonWhiteSpace(publishedRaw, updatedRaw);
        if (candidate is not null &&
            DateTimeOffset.TryParse(
                candidate,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return DateTimeOffset.UnixEpoch;
    }

    private static string? FirstNonWhiteSpace(string? first, string? second)
    {
        if (!string.IsNullOrWhiteSpace(first))
        {
            return first.Trim();
        }

        if (!string.IsNullOrWhiteSpace(second))
        {
            return second.Trim();
        }

        return null;
    }
}
