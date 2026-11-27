using System.Net;
using System.Text;
using System.Text.Json;
using EduScroll.Application.Exceptions;
using EduScroll.Infrastructure.Configuration;
using EduScroll.Infrastructure.VideoDiscovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduScroll.API.IntegrationTests.VideoDiscovery;

public sealed class YouTubeVideoMetadataClientTests
{
    [Fact]
    public async Task GetVideoMetadataAsync_WhenVideoIdsIsNull_ShouldThrowArgumentNullException()
    {
        var client = CreateClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            client.GetVideoMetadataAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task GetVideoMetadataAsync_WhenVideoIdsEmptyOrBlank_ShouldReturnEmptyWithoutHttpCalls()
    {
        var callCount = 0;
        var client = CreateClient((_, _) =>
        {
            callCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }, apiKey: "");

        var result = await client.GetVideoMetadataAsync(["", "   ", "\t"], CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task GetVideoMetadataAsync_WhenApiKeyMissing_ShouldThrowWithoutHttpCalls()
    {
        var callCount = 0;
        var client = CreateClient((_, _) =>
        {
            callCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }, apiKey: "   ");

        var ex = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client.GetVideoMetadataAsync(["dQw4w9WgXcQ"], CancellationToken.None));

        Assert.Equal("YOUTUBE_API_KEY_MISSING", ex.ErrorCode);
        Assert.Equal(0, callCount);
    }

    [Fact]
    public async Task GetVideoMetadataAsync_WithMoreThan50IdsAndDuplicates_ShouldSplitBatchesAndDeduplicate()
    {
        var requestedIdBatches = new List<string[]>();
        var capturedUris = new List<Uri>();

        var client = CreateClient((request, _) =>
        {
            capturedUris.Add(request.RequestUri!);
            var query = ParseQuery(request.RequestUri!);
            Assert.Equal("snippet,contentDetails,statistics", query["part"]);
            Assert.Equal("test-api-key", query["key"]);

            var batchIds = query["id"].Split(',', StringSplitOptions.RemoveEmptyEntries);
            requestedIdBatches.Add(batchIds);

            var payload = JsonSerializer.Serialize(new
            {
                items = batchIds.Select(id => new
                {
                    id,
                    snippet = new
                    {
                        channelId = "UC_channel_1",
                        channelTitle = "Edu Channel",
                        title = $"Title {id}",
                        description = $"Desc {id}",
                        publishedAt = "2026-10-01T12:00:00Z",
                        defaultAudioLanguage = "en",
                        thumbnails = new
                        {
                            @default = new { url = $"https://i.ytimg.com/vi/{id}/default.jpg" },
                            high = new { url = $"https://i.ytimg.com/vi/{id}/hqdefault.jpg" }
                        }
                    },
                    contentDetails = new
                    {
                        duration = "PT1M45S"
                    },
                    statistics = new
                    {
                        viewCount = "1234",
                        likeCount = "56"
                    }
                })
            });

            return Task.FromResult(JsonResponse(HttpStatusCode.OK, payload));
        }, apiKey: "test-api-key");

        var inputIds = Enumerable.Range(1, 52)
            .Select(i => $"vid{i:D2}")
            .Concat(["  VID01  ", "vid02", "", "   "])
            .ToArray();

        var result = await client.GetVideoMetadataAsync(inputIds, CancellationToken.None);

        Assert.Equal(2, requestedIdBatches.Count);
        Assert.Equal(50, requestedIdBatches[0].Length);
        Assert.Equal(2, requestedIdBatches[1].Length);
        Assert.All(capturedUris, uri => Assert.Equal("/youtube/v3/videos", uri.AbsolutePath));
        Assert.Equal(52, result.Count);
        Assert.Equal("https://i.ytimg.com/vi/vid01/hqdefault.jpg", result["vid01"].ThumbnailUrl);
        Assert.Equal(105, result["vid01"].DurationSeconds);
        Assert.Equal(1234L, result["vid52"].ViewCount);
        Assert.Equal(56L, result["vid52"].LikeCount);
    }

    [Theory]
    [InlineData("PT1M45S", 105)]
    [InlineData("PT2H3M4S", 7384)]
    [InlineData("PT30S", 30)]
    [InlineData("P0D", 0)]
    [InlineData("-PT10S", null)]
    [InlineData("INVALID_DURATION", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public async Task GetVideoMetadataAsync_ShouldParseIso8601DurationSafely(
        string? rawDuration,
        int? expectedSeconds)
    {
        var payload = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    id = "durVid1",
                    snippet = new
                    {
                        channelId = "UC1",
                        channelTitle = "Channel",
                        title = "Duration Test",
                        publishedAt = "2026-10-01T12:00:00Z"
                    },
                    contentDetails = new
                    {
                        duration = rawDuration
                    }
                }
            }
        });

        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, payload)));

        var result = await client.GetVideoMetadataAsync(["durVid1"], CancellationToken.None);

        Assert.True(result.TryGetValue("durVid1", out var item));
        Assert.NotNull(item);
        Assert.Equal(expectedSeconds, item.DurationSeconds);
    }

    [Fact]
    public async Task GetVideoMetadataAsync_WhenStatisticsThumbnailsOrLanguageMissing_ShouldHandleGracefully()
    {
        const string payload = """
            {
              "items": [
                {
                  "id": "partial1",
                  "snippet": {
                    "channelId": "UC_part",
                    "channelTitle": "Partial Channel",
                    "title": "Partial Video",
                    "defaultLanguage": "ar"
                  }
                },
                {
                  "id": "partial2",
                  "snippet": {
                    "channelId": "UC_part",
                    "channelTitle": "Partial Channel",
                    "title": "Stats Without LikeCount",
                    "thumbnails": {
                      "medium": { "url": "https://i.ytimg.com/vi/partial2/mqdefault.jpg" },
                      "default": { "url": "https://i.ytimg.com/vi/partial2/default.jpg" }
                    }
                  },
                  "statistics": {
                    "viewCount": 98765
                  }
                }
              ]
            }
            """;

        var client = CreateClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, payload)));

        var result = await client.GetVideoMetadataAsync(["partial1", "partial2"], CancellationToken.None);

        var p1 = result["partial1"];
        Assert.Null(p1.ThumbnailUrl);
        Assert.Null(p1.DurationSeconds);
        Assert.Null(p1.ViewCount);
        Assert.Null(p1.LikeCount);
        Assert.Null(p1.PublishedAtUtc);
        Assert.Equal("ar", p1.DefaultLanguage);

        var p2 = result["partial2"];
        Assert.Equal("https://i.ytimg.com/vi/partial2/mqdefault.jpg", p2.ThumbnailUrl);
        Assert.Equal(98765L, p2.ViewCount);
        Assert.Null(p2.LikeCount);
    }

    [Fact]
    public async Task GetVideoMetadataAsync_On403And500AndTransportErrors_ShouldRedactApiKeyFromLogsAndExceptions()
    {
        const string secretApiKey = "secret-yt-key+plus/slash=98765";
        var escapedKey = Uri.EscapeDataString(secretApiKey);

        // 1. 403 containing the key in upstream response body
        var logger403 = new CapturingLogger<YouTubeVideoMetadataClient>();
        var client403 = CreateClient(
            (_, _) => Task.FromResult(JsonResponse(
                HttpStatusCode.Forbidden,
                JsonSerializer.Serialize(new
                {
                    error = new
                    {
                        message = $"Key invalid: {secretApiKey}",
                        errors = new[] { new { reason = "forbidden" } }
                    }
                }))),
            apiKey: secretApiKey,
            logger: logger403);

        var ex403 = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client403.GetVideoMetadataAsync(["vid1"], CancellationToken.None));
        Assert.Equal("YOUTUBE_API_AUTH_FAILED", ex403.ErrorCode);
        AssertKeyRedacted(ex403.Message, logger403.Messages, secretApiKey, escapedKey);

        // 2. 500 containing the key in upstream body
        var logger500 = new CapturingLogger<YouTubeVideoMetadataClient>();
        var client500 = CreateClient(
            (_, _) => Task.FromResult(JsonResponse(
                HttpStatusCode.InternalServerError,
                $"Upstream error for key={secretApiKey}")),
            apiKey: secretApiKey,
            maxRetries: 0,
            logger: logger500);

        var ex500 = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client500.GetVideoMetadataAsync(["vid1"], CancellationToken.None));
        Assert.Equal("YOUTUBE_API_UNAVAILABLE", ex500.ErrorCode);
        AssertKeyRedacted(ex500.Message, logger500.Messages, secretApiKey, escapedKey);

        // 3. Transport HttpRequestException containing raw and escaped URI/key
        var loggerTransport = new CapturingLogger<YouTubeVideoMetadataClient>();
        var clientTransport = CreateClient(
            (req, _) => throw new HttpRequestException(
                $"Connection failed for {req.RequestUri} with raw {secretApiKey}"),
            apiKey: secretApiKey,
            maxRetries: 0,
            logger: loggerTransport);

        var exTransport = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            clientTransport.GetVideoMetadataAsync(["vid1"], CancellationToken.None));
        Assert.Equal("YOUTUBE_API_UNAVAILABLE", exTransport.ErrorCode);
        AssertKeyRedacted(exTransport.Message, loggerTransport.Messages, secretApiKey, escapedKey);
        Assert.Contains(loggerTransport.Messages, m => m.Contains("[REDACTED]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GetVideoMetadataAsync_On403QuotaExceeded_ShouldNotRetryAndThrowQuotaError()
    {
        var attempts = 0;
        const string quotaPayload = """
            {
              "error": {
                "code": 403,
                "message": "The request cannot be completed because you have exceeded your quota.",
                "errors": [
                  { "domain": "youtube.quota", "reason": "quotaExceeded" }
                ]
              }
            }
            """;

        var client = CreateClient((_, _) =>
        {
            attempts++;
            return Task.FromResult(JsonResponse(HttpStatusCode.Forbidden, quotaPayload));
        }, maxRetries: 2);

        var ex = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client.GetVideoMetadataAsync(["vid1"], CancellationToken.None));

        Assert.Equal(1, attempts);
        Assert.Equal("YOUTUBE_API_QUOTA_EXCEEDED", ex.ErrorCode);
    }

    [Fact]
    public async Task GetVideoMetadataAsync_On401Unauthorized_ShouldNotRetryAndThrowAuthError()
    {
        var attempts = 0;
        var client = CreateClient((_, _) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
        }, maxRetries: 2);

        var ex = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client.GetVideoMetadataAsync(["vid1"], CancellationToken.None));

        Assert.Equal(1, attempts);
        Assert.Equal("YOUTUBE_API_AUTH_FAILED", ex.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task GetVideoMetadataAsync_OnTransient429Or503_ShouldRetryAndSucceed(HttpStatusCode transientStatus)
    {
        var attempts = 0;
        var client = CreateClient((_, _) =>
        {
            attempts++;
            if (attempts == 1)
            {
                return Task.FromResult(new HttpResponseMessage(transientStatus));
            }

            const string okPayload = """{"items":[{"id":"vid1","snippet":{"title":"Recovered"}}]}""";
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, okPayload));
        }, maxRetries: 2);

        var result = await client.GetVideoMetadataAsync(["vid1"], CancellationToken.None);

        Assert.Equal(2, attempts);
        Assert.Equal("Recovered", result["vid1"].Title);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "YOUTUBE_API_RATE_LIMITED")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "YOUTUBE_API_UNAVAILABLE")]
    [InlineData(HttpStatusCode.InternalServerError, "YOUTUBE_API_UNAVAILABLE")]
    [InlineData(HttpStatusCode.BadGateway, "YOUTUBE_API_UNAVAILABLE")]
    [InlineData(HttpStatusCode.GatewayTimeout, "YOUTUBE_API_UNAVAILABLE")]
    public async Task GetVideoMetadataAsync_WhenTransientRetriesExhausted_ShouldThrowExpectedCode(
        HttpStatusCode statusCode,
        string expectedErrorCode)
    {
        var attempts = 0;
        var client = CreateClient((_, _) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(statusCode));
        }, maxRetries: 2);

        var ex = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client.GetVideoMetadataAsync(["vid1"], CancellationToken.None));

        Assert.Equal(3, attempts);
        Assert.Equal(expectedErrorCode, ex.ErrorCode);
    }

    [Fact]
    public async Task GetVideoMetadataAsync_On403RateLimitExceeded_ShouldRetryAndThrowRateLimited()
    {
        var attempts = 0;
        const string rateLimitPayload = """
            {
              "error": {
                "code": 403,
                "message": "Rate limit exceeded.",
                "errors": [
                  { "reason": "userRateLimitExceeded" }
                ]
              }
            }
            """;

        var client = CreateClient((_, _) =>
        {
            attempts++;
            return Task.FromResult(JsonResponse(HttpStatusCode.Forbidden, rateLimitPayload));
        }, maxRetries: 1);

        var ex = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client.GetVideoMetadataAsync(["vid1"], CancellationToken.None));

        Assert.Equal(2, attempts);
        Assert.Equal("YOUTUBE_API_RATE_LIMITED", ex.ErrorCode);
    }

    [Fact]
    public async Task GetVideoMetadataAsync_WhenRequestTimesOut_ShouldRetryAndThrowGatewayTimeout()
    {
        var attempts = 0;
        var client = CreateClient(async (_, ct) =>
        {
            attempts++;
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, timeoutSeconds: 1, maxRetries: 1);

        var ex = await Assert.ThrowsAsync<GatewayTimeoutAppException>(() =>
            client.GetVideoMetadataAsync(["vid1"], CancellationToken.None));

        Assert.Equal(2, attempts);
        Assert.Equal("YOUTUBE_API_TIMEOUT", ex.ErrorCode);
    }

    [Fact]
    public async Task GetVideoMetadataAsync_On408RequestTimeout_ShouldRetryAndThrowGatewayTimeout()
    {
        var attempts = 0;
        var client = CreateClient((_, _) =>
        {
            attempts++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.RequestTimeout));
        }, maxRetries: 1);

        var ex = await Assert.ThrowsAsync<GatewayTimeoutAppException>(() =>
            client.GetVideoMetadataAsync(["vid1"], CancellationToken.None));

        Assert.Equal(2, attempts);
        Assert.Equal("YOUTUBE_API_TIMEOUT", ex.ErrorCode);
    }

    [Fact]
    public async Task GetVideoMetadataAsync_WhenCallerCancels_ShouldAbortImmediatelyWithoutRetrying()
    {
        var attempts = 0;
        using var cts = new CancellationTokenSource();

        var client = CreateClient(async (_, ct) =>
        {
            attempts++;
            cts.Cancel();
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }, timeoutSeconds: 30, maxRetries: 2);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetVideoMetadataAsync(["vid1"], cts.Token));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task GetVideoMetadataAsync_OnMalformedJson_ShouldThrowInvalidResponse()
    {
        var client = CreateClient((_, _) =>
            Task.FromResult(JsonResponse(HttpStatusCode.OK, "{ not-valid-json")));

        var ex = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client.GetVideoMetadataAsync(["vid1"], CancellationToken.None));

        Assert.Equal("YOUTUBE_API_INVALID_RESPONSE", ex.ErrorCode);
    }

    private static YouTubeVideoMetadataClient CreateClient(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler,
        string? apiKey = "test-api-key",
        int timeoutSeconds = 10,
        int maxRetries = 1,
        ILogger<YouTubeVideoMetadataClient>? logger = null)
    {
        var options = Options.Create(new YouTubeOptions
        {
            ApiBaseUrl = "https://www.googleapis.com/youtube/v3",
            RssBaseUrl = "https://www.youtube.com/feeds/videos.xml",
            ApiKey = apiKey,
            TimeoutSeconds = timeoutSeconds,
            MaxRetries = maxRetries,
            RetryBaseDelayMilliseconds = 5,
            MetadataBatchSize = 50,
            MaxVideosPerChannelPerSync = 15
        });

        var httpClient = new HttpClient(new StubHttpMessageHandler(handler));
        return new YouTubeVideoMetadataClient(
            httpClient,
            options,
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<YouTubeVideoMetadataClient>.Instance);
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

            var key = Uri.UnescapeDataString(pair[..idx]);
            var value = Uri.UnescapeDataString(pair[(idx + 1)..]);
            result[key] = value;
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
