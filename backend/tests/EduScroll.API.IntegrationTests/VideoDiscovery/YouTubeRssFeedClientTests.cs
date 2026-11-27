using System.Net;
using System.Text;
using EduScroll.Application.Exceptions;
using EduScroll.Infrastructure.Configuration;
using EduScroll.Infrastructure.VideoDiscovery;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EduScroll.API.IntegrationTests.VideoDiscovery;

public sealed class YouTubeRssFeedClientTests
{
    private const string SampleChannelId = "UCYO_jab_esuFRV4b17AJtAw";

    [Fact]
    public async Task FetchChannelVideosAsync_WithValidAtomFeedAndOptionalMediaFields_ShouldParseAllEntries()
    {
        const string atomXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns:yt="http://www.youtube.com/xml/schemas/2015"
                  xmlns:media="http://search.yahoo.com/mrss/"
                  xmlns="http://www.w3.org/2005/Atom">
              <id>yt:channel:UCYO_jab_esuFRV4b17AJtAw</id>
              <yt:channelId>UCYO_jab_esuFRV4b17AJtAw</yt:channelId>
              <title>3Blue1Brown</title>
              <author>
                <name>3Blue1Brown</name>
              </author>
              <entry>
                <id>yt:video:aircAruvnKk</id>
                <yt:videoId>aircAruvnKk</yt:videoId>
                <yt:channelId>UCYO_jab_esuFRV4b17AJtAw</yt:channelId>
                <title>  But what is a neural network?  </title>
                <author>
                  <name>3Blue1Brown</name>
                </author>
                <published>2026-11-20T15:30:00+02:00</published>
                <updated>2026-11-21T08:00:00+00:00</updated>
                <media:group>
                  <media:title>But what is a neural network?</media:title>
                  <media:thumbnail url="https://i2.ytimg.com/vi/aircAruvnKk/hqdefault.jpg" width="480" height="360"/>
                  <media:description>  Visual introduction to deep learning.  </media:description>
                  <media:community>
                    <media:statistics views="15234567"/>
                  </media:community>
                </media:group>
              </entry>
              <entry>
                <id>yt:video:IHZwWFHWa-w</id>
                <title>Gradient descent, how neural networks learn</title>
                <updated>2026-11-22T10:15:00Z</updated>
              </entry>
            </feed>
            """;

        Uri? capturedUri = null;
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            capturedUri = req.RequestUri;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(atomXml, Encoding.UTF8, "application/atom+xml")
            });
        });

        var client = CreateClient(handler);

        var items = await client.FetchChannelVideosAsync(SampleChannelId, CancellationToken.None);

        Assert.NotNull(capturedUri);
        Assert.Equal(
            $"https://www.youtube.com/feeds/videos.xml?channel_id={SampleChannelId}",
            capturedUri!.AbsoluteUri);

        Assert.Equal(2, items.Count);

        var first = items[0];
        Assert.Equal("aircAruvnKk", first.YouTubeVideoId);
        Assert.Equal(SampleChannelId, first.ChannelExternalId);
        Assert.Equal("3Blue1Brown", first.ChannelName);
        Assert.Equal("But what is a neural network?", first.Title);
        Assert.Equal("Visual introduction to deep learning.", first.Description);
        Assert.Equal("https://i2.ytimg.com/vi/aircAruvnKk/hqdefault.jpg", first.ThumbnailUrl);
        Assert.Equal(15_234_567L, first.ViewCount);
        Assert.Equal(new DateTimeOffset(2026, 11, 20, 13, 30, 0, TimeSpan.Zero), first.PublishedAtUtc);

        var second = items[1];
        Assert.Equal("IHZwWFHWa-w", second.YouTubeVideoId);
        Assert.Equal(SampleChannelId, second.ChannelExternalId);
        Assert.Equal("3Blue1Brown", second.ChannelName);
        Assert.Equal("Gradient descent, how neural networks learn", second.Title);
        Assert.Equal(string.Empty, second.Description);
        Assert.Null(second.ThumbnailUrl);
        Assert.Equal(0L, second.ViewCount);
        Assert.Equal(new DateTimeOffset(2026, 11, 22, 10, 15, 0, TimeSpan.Zero), second.PublishedAtUtc);
    }

    [Fact]
    public async Task FetchChannelVideosAsync_WithEmptyAtomFeed_ShouldReturnEmptyList()
    {
        const string emptyFeedXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns:yt="http://www.youtube.com/xml/schemas/2015"
                  xmlns:media="http://search.yahoo.com/mrss/"
                  xmlns="http://www.w3.org/2005/Atom">
              <id>yt:channel:UC_empty</id>
              <yt:channelId>UC_empty</yt:channelId>
              <title>Empty Channel</title>
            </feed>
            """;

        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(emptyFeedXml, Encoding.UTF8, "application/atom+xml")
            }));

        var client = CreateClient(handler);

        var items = await client.FetchChannelVideosAsync("UC_empty", CancellationToken.None);

        Assert.Empty(items);
    }

    [Theory]
    [InlineData("<feed xmlns=\"http://www.w3.org/2005/Atom\"><entry><title>unclosed")]
    [InlineData("<html><body>Not an Atom feed</body></html>")]
    [InlineData("<rss version=\"2.0\"><channel><title>Wrong root</title></channel></rss>")]
    [InlineData("")]
    public async Task FetchChannelVideosAsync_WithMalformedOrUnexpectedXml_ShouldThrowInvalidXmlException(string body)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/xml")
            }));

        var client = CreateClient(handler, maxRetries: 2);

        var ex = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client.FetchChannelVideosAsync(SampleChannelId, CancellationToken.None));

        Assert.Equal("YOUTUBE_RSS_INVALID_XML", ex.ErrorCode);
        Assert.Equal(1, handler.Attempts);
    }

    [Theory]
    [InlineData("""<?xml version="1.0"?><!DOCTYPE foo [<!ENTITY xxe SYSTEM "file:///etc/passwd">]><feed xmlns="http://www.w3.org/2005/Atom"><title>&xxe;</title></feed>""")]
    [InlineData("""<?xml version="1.0"?><!DOCTYPE feed><feed xmlns="http://www.w3.org/2005/Atom"><title>Test</title></feed>""")]
    public async Task FetchChannelVideosAsync_WithXxeOrDoctypePayload_ShouldRejectWithInvalidXmlException(string payload)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(payload, Encoding.UTF8, "application/xml")
            }));

        var client = CreateClient(handler);

        var ex = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client.FetchChannelVideosAsync(SampleChannelId, CancellationToken.None));

        Assert.Equal("YOUTUBE_RSS_INVALID_XML", ex.ErrorCode);
        Assert.Equal(1, handler.Attempts);
    }

    [Fact]
    public async Task FetchChannelVideosAsync_OnTransient503_ShouldRetryWithFreshRequestsAndSucceed()
    {
        const string validXml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <feed xmlns:yt="http://www.youtube.com/xml/schemas/2015"
                  xmlns:media="http://search.yahoo.com/mrss/"
                  xmlns="http://www.w3.org/2005/Atom">
              <entry>
                <yt:videoId>vidRetry001</yt:videoId>
                <yt:channelId>UC_retry</yt:channelId>
                <title>Recovered Video</title>
                <author><name>Edu Channel</name></author>
                <published>2026-11-25T12:00:00Z</published>
              </entry>
            </feed>
            """;

        var seenRequests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler((req, _) =>
        {
            seenRequests.Add(req);
            if (seenRequests.Count == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(validXml, Encoding.UTF8, "application/atom+xml")
            });
        });

        var client = CreateClient(handler, maxRetries: 2);

        var items = await client.FetchChannelVideosAsync("UC_retry", CancellationToken.None);

        Assert.Equal(2, handler.Attempts);
        Assert.Equal(2, seenRequests.Count);
        Assert.NotSame(seenRequests[0], seenRequests[1]);
        var item = Assert.Single(items);
        Assert.Equal("vidRetry001", item.YouTubeVideoId);
    }

    [Fact]
    public async Task FetchChannelVideosAsync_OnHttp404_ShouldNotRetryAndThrowFetchFailed()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var client = CreateClient(handler, maxRetries: 2);

        var ex = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client.FetchChannelVideosAsync(SampleChannelId, CancellationToken.None));

        Assert.Equal("YOUTUBE_RSS_FETCH_FAILED", ex.ErrorCode);
        Assert.Equal(1, handler.Attempts);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadGateway)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task FetchChannelVideosAsync_WhenTransient5xxExhaustsRetries_ShouldThrowFetchFailed(HttpStatusCode statusCode)
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(statusCode)));

        var client = CreateClient(handler, maxRetries: 2);

        var ex = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client.FetchChannelVideosAsync(SampleChannelId, CancellationToken.None));

        Assert.Equal("YOUTUBE_RSS_FETCH_FAILED", ex.ErrorCode);
        Assert.Equal(3, handler.Attempts);
    }

    [Fact]
    public async Task FetchChannelVideosAsync_WhenTransportThrowsHttpRequestException_ShouldRetryAndThrowFetchFailed()
    {
        var handler = new StubHttpMessageHandler((_, _) =>
            throw new HttpRequestException("Simulated DNS failure"));

        var client = CreateClient(handler, maxRetries: 1);

        var ex = await Assert.ThrowsAsync<ExternalDependencyAppException>(() =>
            client.FetchChannelVideosAsync(SampleChannelId, CancellationToken.None));

        Assert.Equal("YOUTUBE_RSS_FETCH_FAILED", ex.ErrorCode);
        Assert.Equal(2, handler.Attempts);
    }

    [Fact]
    public async Task FetchChannelVideosAsync_WhenRequestTimesOut_ShouldRetryAndThrowGatewayTimeout()
    {
        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = CreateClient(handler, timeoutSeconds: 1, maxRetries: 1);

        var ex = await Assert.ThrowsAsync<GatewayTimeoutAppException>(() =>
            client.FetchChannelVideosAsync(SampleChannelId, CancellationToken.None));

        Assert.Equal("YOUTUBE_RSS_TIMEOUT", ex.ErrorCode);
        Assert.Equal(2, handler.Attempts);
    }

    [Fact]
    public async Task FetchChannelVideosAsync_WhenCallerCancels_ShouldAbortImmediatelyWithoutRetrying()
    {
        using var cts = new CancellationTokenSource();

        var handler = new StubHttpMessageHandler(async (_, ct) =>
        {
            cts.Cancel();
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var client = CreateClient(handler, timeoutSeconds: 30, maxRetries: 2);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.FetchChannelVideosAsync(SampleChannelId, cts.Token));

        Assert.Equal(1, handler.Attempts);
    }

    private static YouTubeRssFeedClient CreateClient(
        StubHttpMessageHandler handler,
        int timeoutSeconds = 15,
        int maxRetries = 2)
    {
        var httpClient = new HttpClient(handler);
        var options = Options.Create(new YouTubeOptions
        {
            RssBaseUrl = "https://www.youtube.com/feeds/videos.xml",
            TimeoutSeconds = timeoutSeconds,
            MaxRetries = maxRetries,
            RetryBaseDelayMilliseconds = 5
        });

        return new YouTubeRssFeedClient(
            httpClient,
            options,
            NullLogger<YouTubeRssFeedClient>.Instance);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        public int Attempts { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Attempts++;
            return handler(request, cancellationToken);
        }
    }
}
