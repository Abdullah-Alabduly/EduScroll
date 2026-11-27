using System.Net;
using System.Text;
using System.Text.Json;
using EduScroll.Application.Exceptions;
using EduScroll.Infrastructure.Configuration;
using EduScroll.Infrastructure.VideoDiscovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduScroll.API.IntegrationTests.VideoDiscovery;

public sealed class YouTubeSearchFallbackClientTests
{
    [Fact]
    public async Task SearchVideosAsync_WithValidResponse_ShouldSendRequiredQueryParamsAndDecodeResults()
    {
        Uri? capturedUri = null;

        const string searchJson = """
            {
              "items": [
                {
                  "id": { "kind": "youtube#video", "videoId": "vidSearch1" },
                  "snippet": {
                    "publishedAt": "2026-11-20T10:00:00Z",
                    "channelId": "UC_MATH_1",
                    "title": "Linear Algebra &amp; Eigenvalues",
                    "description": "Learn &quot;eigenvectors&quot; step by step.",
                    "channelTitle": "Math &amp; Code",
                    "thumbnails": {
                      "default": { "url": "https://i.ytimg.com/vi/vidSearch1/default.jpg" },
                      "high": { "url": "https://i.ytimg.com/vi/vidSearch1/hqdefault.jpg" }
                    }
                  }
                },
                {
                  "id": { "kind": "youtube#video", "videoId": "vidSearch1" },
                  "snippet": {
                    "channelId": "UC_MATH_1",
                    "title": "Duplicate video id should be deduplicated"
                  }
                },
                {
                  "id": { "kind": "youtube#channel", "channelId": "UC_IGNORED" },
                  "snippet": {
                    "channelId": "UC_IGNORED",
                    "title": "Channel result without videoId"
                  }
                }
              ]
            }
            """;

        var client = CreateClient((req, _) =>
        {
            capturedUri = req.RequestUri;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, searchJson));
        }, apiKey: "test-search-key");

        var items = await client.SearchVideosAsync("eigenvalues visual", "EN", 10, CancellationToken.None);

        Assert.NotNull(capturedUri);
        Assert.Equal("/youtube/v3/search", capturedUri!.AbsolutePath);

        var query = ParseQuery(capturedUri);
        Assert.Equal("snippet", query["part"]);
        Assert.Equal("video", query["type"]);
        Assert.Equal("true", query["videoEmbeddable"]);
        Assert.Equal("strict", query["safeSearch"]);
        Assert.Equal("short", query["videoDuration"]);
        Assert.Equal("en", query["relevanceLanguage"]);
        Assert.Equal("eigenvalues visual", query["q"]);
        Assert.Equal("10", query["maxResults"]);
        Assert.Equal("test-search-key", query["key"]);

        var item = Assert.Single(items);
        Assert.Equal("vidSearch1", item.YouTubeVideoId);
        Assert.Equal("UC_MATH_1", item.ChannelExternalId);
        Assert.Equal("Math & Code", item.ChannelName);
        Assert.Equal("Linear Algebra & Eigenvalues", item.Title);
        Assert.Equal("Learn \"eigenvectors\" step by step.", item.Description);
        Assert.Equal("https://i.ytimg.com/vi/vidSearch1/hqdefault.jpg", item.ThumbnailUrl);
        Assert.Equal("en", item.Language);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 10, 0, 0, TimeSpan.Zero), item.PublishedAtUtc);
    }

    [Fact]
    public async Task SearchVideosAsync_WithAnyOrCustomDuration_ShouldOmitOrNormalizeVideoDurationQueryParam()
    {
        Uri? anyUri = null;
        var anyClient = CreateClient((req, _) =>
        {
            anyUri = req.RequestUri;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"items":[]}"""));
        }, searchVideoDuration: "any");

        await anyClient.SearchVideosAsync("linear algebra", "en", 5, CancellationToken.None);
        Assert.NotNull(anyUri);
        var anyQuery = ParseQuery(anyUri!);
        Assert.False(anyQuery.ContainsKey("videoDuration"));

        Uri? mediumUri = null;
        var mediumClient = CreateClient((req, _) =>
        {
            mediumUri = req.RequestUri;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """{"items":[]}"""));
        }, searchVideoDuration: " MEDIUM ");

        await mediumClient.SearchVideosAsync("linear algebra", "en", 5, CancellationToken.None);
        Assert.NotNull(mediumUri);
        var mediumQuery = ParseQuery(mediumUri!);
        Assert.Equal("medium", mediumQuery["videoDuration"]);
    }

    [Fact]
    public async Task SearchVideosAsync_WhenApiKeyMissing_ShouldThrowWithoutHttpCalls()
    {
        var callCount = 0;
        var client = CreateClient((_, _) =>
        {
            callCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }, apiKey: "   ");

        var ex = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client.SearchVideosAsync("calculus", "en", 10, CancellationToken.None));

        Assert.Equal("YOUTUBE_API_KEY_MISSING", ex.ErrorCode);
        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task SearchVideosAsync_OnErrors_ShouldRedactApiKeyFromLogsAndExceptions()
    {
        const string secretApiKey = "secret-search-key+plus/slash=12345";
        var escapedKey = Uri.EscapeDataString(secretApiKey);

        var logger403 = new CapturingLogger<YouTubeSearchFallbackClient>();
        var client403 = CreateClient(
            (_, _) => Task.FromResult(JsonResponse(
                HttpStatusCode.Forbidden,
                JsonSerializer.Serialize(new
                {
                    error = new
                    {
                        message = $"Invalid key: {secretApiKey}",
                        errors = new[] { new { reason = "forbidden" } }
                    }
                }))),
            apiKey: secretApiKey,
            logger: logger403);

        var ex403 = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client403.SearchVideosAsync("physics", "en", 5, CancellationToken.None));
        Assert.Equal("YOUTUBE_API_AUTH_FAILED", ex403.ErrorCode);
        AssertKeyRedacted(ex403.Message, logger403.Messages, secretApiKey, escapedKey);

        var loggerTransport = new CapturingLogger<YouTubeSearchFallbackClient>();
        var clientTransport = CreateClient(
            (req, _) => throw new HttpRequestException($"Request failed for {req.RequestUri} with key {secretApiKey}"),
            apiKey: secretApiKey,
            maxRetries: 0,
            logger: loggerTransport);

        var exTransport = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            clientTransport.SearchVideosAsync("physics", "ar", 5, CancellationToken.None));
        Assert.Equal("YOUTUBE_API_UNAVAILABLE", exTransport.ErrorCode);
        AssertKeyRedacted(exTransport.Message, loggerTransport.Messages, secretApiKey, escapedKey);
        Assert.Contains(loggerTransport.Messages, m => m.Contains("[REDACTED]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchVideosAsync_On403QuotaExceeded_ShouldNotRetryAndThrowQuotaExceeded()
    {
        var attempts = 0;
        var quotaBody = JsonSerializer.Serialize(new
        {
            error = new
            {
                code = 403,
                message = "The request cannot be completed because you have exceeded your quota.",
                errors = new[] { new { domain = "youtube.quota", reason = "quotaExceeded" } }
            }
        });

        var client = CreateClient((_, _) =>
        {
            attempts++;
            return Task.FromResult(JsonResponse(HttpStatusCode.Forbidden, quotaBody));
        }, maxRetries: 2);

        var ex = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client.SearchVideosAsync("chemistry", "en", 10, CancellationToken.None));

        Assert.Equal(1, attempts);
        Assert.Equal("YOUTUBE_API_QUOTA_EXCEEDED", ex.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "YOUTUBE_API_RATE_LIMITED")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "YOUTUBE_API_UNAVAILABLE")]
    [InlineData(HttpStatusCode.InternalServerError, "YOUTUBE_API_UNAVAILABLE")]
    public async Task SearchVideosAsync_OnTransientErrors_ShouldRetryAndMapFailureCode(
        HttpStatusCode statusCode,
        string expectedCode)
    {
        var attempts = 0;
        var client = CreateClient((_, _) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }, maxRetries: 2);

        var ex = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client.SearchVideosAsync("biology", "ar", 10, CancellationToken.None));

        Assert.Equal(3, attempts);
        Assert.Equal(expectedCode, ex.ErrorCode);
    }

    [Fact]
    public async Task SearchVideosAsync_WhenRequestTimesOutOrCallerCancels_ShouldBehaveCleanly()
    {
        var timeoutAttempts = 0;
        var timeoutClient = CreateClient(async (_, ct) =>
        {
            timeoutAttempts++;
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, timeoutSeconds: 1, maxRetries: 1);

        var timeoutEx = await Assert.ThrowsAsync<GatewayTimeoutAppException>(() =>
            timeoutClient.SearchVideosAsync("math", "en", 5, CancellationToken.None));
        Assert.Equal("YOUTUBE_API_TIMEOUT", timeoutEx.ErrorCode);
        Assert.Equal(2, timeoutAttempts);

        var cancelAttempts = 0;
        using var cts = new CancellationTokenSource();
        var cancelClient = CreateClient(async (_, ct) =>
        {
            cancelAttempts++;
            cts.Cancel();
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, timeoutSeconds: 30, maxRetries: 2);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelClient.SearchVideosAsync("math", "en", 5, cts.Token));
        Assert.Equal(1, cancelAttempts);
    }

    private static YouTubeSearchFallbackClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        string? apiKey = "test-api-key",
        int timeoutSeconds = 10,
        int maxRetries = 1,
        string searchVideoDuration = "short",
        ILogger<YouTubeSearchFallbackClient>? logger = null)
    {
        var options = Options.Create(new YouTubeOptions
        {
            ApiBaseUrl = "https://www.googleapis.com/youtube/v3",
            RssBaseUrl = "https://www.youtube.com/feeds/videos.xml",
            ApiKey = apiKey,
            TimeoutSeconds = timeoutSeconds,
            MaxRetries = maxRetries,
            RetryBaseDelayMilliseconds = 5,
            SearchVideoDuration = searchVideoDuration
        });

        var httpClient = new HttpClient(new StubHttpMessageHandler(handler));
        return new YouTubeSearchFallbackClient(
            httpClient,
            options,
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<YouTubeSearchFallbackClient>.Instance);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string content) =>
        new(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };

    private static Dictionary<string, string> ParseQuery(Uri uri)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var query = uri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=', StringComparison.Ordinal);
            if (idx < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
                continue;
            }

            result[Uri.UnescapeDataString(pair[..idx])] = Uri.UnescapeDataString(pair[(idx + 1)..]);
        }

        return result;
    }

    private static void AssertKeyRedacted(
        string exceptionMessage,
        IReadOnlyCollection<string> logMessages,
        string rawKey,
        string escapedKey)
    {
        Assert.DoesNotContain(rawKey, exceptionMessage, StringComparison.Ordinal);
        Assert.DoesNotContain(escapedKey, exceptionMessage, StringComparison.OrdinalIgnoreCase);

        foreach (var msg in logMessages)
        {
            Assert.DoesNotContain(rawKey, msg, StringComparison.Ordinal);
            Assert.DoesNotContain(escapedKey, msg, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var rendered = formatter(state, exception);
            if (exception is not null)
            {
                rendered = $"{rendered} | {exception}";
            }

            Messages.Add(rendered);
        }
    }
}
