using System.Net;
using System.Net.Http.Headers;
using System.Text;
using EduScroll.Application.Exceptions;
using EduScroll.Infrastructure.Configuration;
using EduScroll.Infrastructure.WebExtraction;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EduScroll.API.IntegrationTests.WebExtraction;

public sealed class WebExtractionSecurityTests
{
    private static readonly IPAddress PublicIpv4 = IPAddress.Parse("93.184.216.34");
    private static readonly IPAddress PublicIpv6 = IPAddress.Parse("2606:2800:220:1:248:1893:25c8:1946");

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.1.5")]
    [InlineData("0.0.0.0")]
    [InlineData("10.0.0.1")]
    [InlineData("10.255.255.254")]
    [InlineData("100.64.0.1")]
    [InlineData("169.254.169.254")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.254")]
    [InlineData("192.168.0.1")]
    [InlineData("192.168.1.42")]
    [InlineData("198.18.0.1")]
    [InlineData("224.0.0.1")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    [InlineData("::1")]
    [InlineData("::")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456:789a::1")]
    [InlineData("fe80::1")]
    [InlineData("ff02::1")]
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:169.254.169.254")]
    [InlineData("::ffff:10.0.0.5")]
    [InlineData("64:ff9b::10.0.0.1")]
    [InlineData("2002:7f00:0001::1")]
    public void SsrfUrlGuard_ShouldRejectForbiddenIpAddresses(string ipString)
    {
        var ip = IPAddress.Parse(ipString);
        Assert.True(SsrfUrlGuard.IsForbiddenIpAddress(ip), $"Expected {ipString} to be rejected.");
    }

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("1.1.1.1")]
    [InlineData("8.8.8.8")]
    [InlineData("2606:2800:220:1:248:1893:25c8:1946")]
    public void SsrfUrlGuard_ShouldAllowPublicIpAddresses(string ipString)
    {
        var ip = IPAddress.Parse(ipString);
        Assert.False(SsrfUrlGuard.IsForbiddenIpAddress(ip), $"Expected {ipString} to be allowed.");
    }

    [Theory]
    [InlineData("http://localhost/admin")]
    [InlineData("http://sub.localhost/test")]
    [InlineData("http://127.0.0.1/secret")]
    [InlineData("http://169.254.169.254/latest/meta-data/")]
    [InlineData("http://[::1]/secret")]
    [InlineData("http://service.internal/api")]
    [InlineData("http://printer.local/status")]
    [InlineData("http://metadata.google.internal/computeMetadata/v1/")]
    [InlineData("http://intranet/docs")]
    [InlineData("https://user:pass@example.org/article")]
    public void SsrfUrlGuard_ShouldRejectInternalUrlsAndCredentials(string rawUrl)
    {
        var ex = Assert.Throws<ValidationAppException>(() =>
            SsrfUrlGuard.ValidateUriSyntaxAndHost(new Uri(rawUrl)));

        Assert.Equal("URL_SSRF_BLOCKED", ex.ErrorCode);
    }

    [Theory]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.org/file.txt")]
    [InlineData("gopher://example.org/1")]
    public void SsrfUrlGuard_ShouldRejectNonHttpSchemes(string rawUrl)
    {
        var ex = Assert.Throws<ValidationAppException>(() =>
            SsrfUrlGuard.ValidateUriSyntaxAndHost(new Uri(rawUrl)));

        Assert.Equal("URL_INVALID_SCHEME", ex.ErrorCode);
    }

    [Fact]
    public async Task SsrfUrlGuard_WhenDnsResolvesToPrivateIp_ShouldBlock()
    {
        var dns = new FakeDnsResolver(new Dictionary<string, IPAddress[]>
        {
            ["rebind.example.org"] = [PublicIpv4, IPAddress.Parse("10.0.0.12")]
        });

        var ex = await Assert.ThrowsAsync<ValidationAppException>(() =>
            SsrfUrlGuard.ValidateResolvedAddressesAsync(
                new Uri("https://rebind.example.org/article"),
                dns,
                isRedirect: false,
                CancellationToken.None));

        Assert.Equal("URL_SSRF_BLOCKED", ex.ErrorCode);
        Assert.DoesNotContain("10.0.0.12", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAndExtractAsync_WhenRedirectTargetsPrivateIp_ShouldBlockRedirect()
    {
        var dns = new FakeDnsResolver(new Dictionary<string, IPAddress[]>
        {
            ["public.example.org"] = [PublicIpv4]
        });

        var transport = new FakeWebHttpTransport(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("http://169.254.169.254/latest/meta-data/");
            return Task.FromResult(redirect);
        });

        var fetcher = CreateFetcher(transport, dns);

        var ex = await Assert.ThrowsAsync<ValidationAppException>(() =>
            fetcher.FetchAndExtractAsync(new Uri("https://public.example.org/go"), CancellationToken.None));

        Assert.Equal("URL_REDIRECT_BLOCKED", ex.ErrorCode);
        Assert.DoesNotContain("169.254.169.254", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAndExtractAsync_WhenRedirectLimitExceeded_ShouldThrowUnprocessableEntity()
    {
        var dns = new FakeDnsResolver(new Dictionary<string, IPAddress[]>
        {
            ["public.example.org"] = [PublicIpv4]
        });

        var hop = 0;
        var transport = new FakeWebHttpTransport(_ =>
        {
            hop++;
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri($"https://public.example.org/step-{hop}");
            return Task.FromResult(redirect);
        });

        var fetcher = CreateFetcher(transport, dns, maxRedirects: 2);

        var ex = await Assert.ThrowsAsync<UnprocessableEntityAppException>(() =>
            fetcher.FetchAndExtractAsync(new Uri("https://public.example.org/start"), CancellationToken.None));

        Assert.Equal("URL_TOO_MANY_REDIRECTS", ex.ErrorCode);
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/png")]
    [InlineData("application/octet-stream")]
    [InlineData("application/json")]
    public async Task FetchAndExtractAsync_WithUnsupportedContentType_ShouldThrowUnprocessableEntity(string mediaType)
    {
        var dns = new FakeDnsResolver(new Dictionary<string, IPAddress[]>
        {
            ["public.example.org"] = [PublicIpv4]
        });

        var transport = new FakeWebHttpTransport(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([1, 2, 3, 4])
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            return Task.FromResult(response);
        });

        var fetcher = CreateFetcher(transport, dns);

        var ex = await Assert.ThrowsAsync<UnprocessableEntityAppException>(() =>
            fetcher.FetchAndExtractAsync(new Uri("https://public.example.org/file"), CancellationToken.None));

        Assert.Equal("URL_UNSUPPORTED_CONTENT_TYPE", ex.ErrorCode);
    }

    [Fact]
    public async Task FetchAndExtractAsync_WhenStreamExceedsMaxBytesWithoutContentLength_ShouldThrowPayloadTooLarge()
    {
        var dns = new FakeDnsResolver(new Dictionary<string, IPAddress[]>
        {
            ["public.example.org"] = [PublicIpv4]
        });

        var transport = new FakeWebHttpTransport(_ =>
        {
            var oversizedPayload = Encoding.UTF8.GetBytes($"<html><body><article><p>{new string('a', 5000)}</p></article></body></html>");
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StreamContent(new MemoryStream(oversizedPayload))
            };
            response.Content.Headers.ContentType = new MediaTypeHeaderValue("text/html");
            return Task.FromResult(response);
        });

        var fetcher = CreateFetcher(transport, dns, maxResponseBytes: 2048);

        var ex = await Assert.ThrowsAsync<PayloadTooLargeAppException>(() =>
            fetcher.FetchAndExtractAsync(new Uri("https://public.example.org/huge"), CancellationToken.None));

        Assert.Equal("URL_RESPONSE_TOO_LARGE", ex.ErrorCode);
    }

    [Fact]
    public async Task FetchAndExtractAsync_WhenRequestTimesOut_ShouldThrowGatewayTimeout()
    {
        var dns = new FakeDnsResolver(new Dictionary<string, IPAddress[]>
        {
            ["slow.example.org"] = [PublicIpv6]
        });

        var transport = new FakeWebHttpTransport(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        var fetcher = CreateFetcher(transport, dns, requestTimeoutSeconds: 1);

        var ex = await Assert.ThrowsAsync<GatewayTimeoutAppException>(() =>
            fetcher.FetchAndExtractAsync(new Uri("https://slow.example.org/slow"), CancellationToken.None));

        Assert.Equal("URL_TIMEOUT", ex.ErrorCode);
    }

    [Fact]
    public void HtmlArticleExtractor_ShouldStripBoilerplateScriptsAdsAndPreserveParagraphs()
    {
        const string html = """
            <!doctype html>
            <html>
              <head>
                <title>Site Name | Neural Networks</title>
                <meta property="og:title" content="How Neural Networks Learn" />
                <style>.hero { color: red; }</style>
                <script>alert('xss');</script>
              </head>
              <body>
                <header><nav><a href="/">Home</a><a href="/pricing">Pricing</a></nav></header>
                <div class="cookie-banner">Accept cookies now!</div>
                <aside class="sidebar">Subscribe to our newsletter</aside>
                <main>
                  <article>
                    <h1>How Neural Networks Learn</h1>
                    <p>Backpropagation computes gradients of the loss function with respect to each weight in the network.</p>
                    <div class="ad-container">Buy widgets today!</div>
                    <p>Gradient descent then updates those weights iteratively to reduce prediction error on training examples.</p>
                  </article>
                </main>
                <footer>Copyright 2026 Example Corp</footer>
              </body>
            </html>
            """;

        var extractor = new HtmlArticleExtractor();
        var result = extractor.Extract(html, new Uri("https://example.org/nn"));

        Assert.Equal("How Neural Networks Learn", result.Title);
        Assert.Contains("Backpropagation computes gradients", result.Text, StringComparison.Ordinal);
        Assert.Contains("Gradient descent then updates", result.Text, StringComparison.Ordinal);
        Assert.Contains("\n\n", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("alert('xss')", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Accept cookies", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Buy widgets", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Copyright 2026", result.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Pricing", result.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FetchAndExtractAsync_FollowingValidRedirect_ShouldReturnFinalUrlAndExtractedArticle()
    {
        var dns = new FakeDnsResolver(new Dictionary<string, IPAddress[]>
        {
            ["short.example.org"] = [PublicIpv4],
            ["docs.example.org"] = [PublicIpv4]
        });

        var transport = new FakeWebHttpTransport(request =>
        {
            if (request.RequestUri!.Host == "short.example.org")
            {
                var redirect = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
                redirect.Headers.Location = new Uri("https://docs.example.org/articles/clean-code");
                return Task.FromResult(redirect);
            }

            var html = """
                <html>
                  <head><title>Clean Architecture Overview</title></head>
                  <body>
                    <article>
                      <h1>Clean Architecture Overview</h1>
                      <p>Clean Architecture separates domain rules from delivery mechanisms and persistence details so systems stay testable.</p>
                    </article>
                  </body>
                </html>
                """;

            var ok = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(html, Encoding.UTF8, "text/html")
            };
            return Task.FromResult(ok);
        });

        var fetcher = CreateFetcher(transport, dns);
        var fetched = await fetcher.FetchAndExtractAsync(
            new Uri("https://short.example.org/clean"),
            CancellationToken.None);

        Assert.Equal("https://docs.example.org/articles/clean-code", fetched.FinalUrl);
        Assert.Equal("Clean Architecture Overview", fetched.Title);
        Assert.Contains("Clean Architecture separates domain rules", fetched.ExtractedText, StringComparison.Ordinal);
    }

    private static SecureWebContentFetcher CreateFetcher(
        IWebHttpTransport transport,
        IDnsResolver dnsResolver,
        int maxRedirects = 3,
        int maxResponseBytes = 2 * 1024 * 1024,
        int requestTimeoutSeconds = 10)
    {
        var options = Options.Create(new SourceIngestionOptions
        {
            WebFetch = new WebFetchOptions
            {
                ConnectTimeoutSeconds = 5,
                RequestTimeoutSeconds = requestTimeoutSeconds,
                MaxResponseBytes = maxResponseBytes,
                MaxRedirects = maxRedirects,
                UserAgent = "EduScrollTestBot/1.0",
                AllowedContentTypes = ["text/html", "application/xhtml+xml"]
            }
        });

        return new SecureWebContentFetcher(
            transport,
            dnsResolver,
            new HtmlArticleExtractor(),
            options,
            NullLogger<SecureWebContentFetcher>.Instance);
    }

    private sealed class FakeDnsResolver(Dictionary<string, IPAddress[]> records) : IDnsResolver
    {
        public Task<IPAddress[]> GetHostAddressesAsync(string host, CancellationToken cancellationToken)
        {
            if (records.TryGetValue(host, out var ips))
            {
                return Task.FromResult(ips);
            }

            return Task.FromResult(Array.Empty<IPAddress>());
        }
    }

    private sealed class FakeWebHttpTransport : IWebHttpTransport
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler;

        public FakeWebHttpTransport(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = (req, _) => handler(req);
        }

        public FakeWebHttpTransport(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _handler(request, cancellationToken);
    }
}
