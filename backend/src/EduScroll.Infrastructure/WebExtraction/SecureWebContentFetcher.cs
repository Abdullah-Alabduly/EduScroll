using System.Net;
using System.Net.Http.Headers;
using System.Text;
using EduScroll.Application.Exceptions;
using EduScroll.Application.Sources.Models;
using EduScroll.Application.Sources.Ports;
using EduScroll.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduScroll.Infrastructure.WebExtraction;

public sealed class SecureWebContentFetcher(
    IWebHttpTransport httpTransport,
    IDnsResolver dnsResolver,
    HtmlArticleExtractor htmlArticleExtractor,
    IOptions<SourceIngestionOptions> options,
    ILogger<SecureWebContentFetcher> logger) : IWebContentFetcher
{
    private static readonly HashSet<HttpStatusCode> RedirectStatusCodes =
    [
        HttpStatusCode.MovedPermanently,
        HttpStatusCode.Found,
        HttpStatusCode.SeeOther,
        HttpStatusCode.TemporaryRedirect,
        HttpStatusCode.PermanentRedirect
    ];

    public async Task<WebFetchedArticle> FetchAndExtractAsync(
        Uri url,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(url);

        var webFetch = options.Value.WebFetch;
        var allowedContentTypes = new HashSet<string>(
            webFetch.AllowedContentTypes.Select(ct => ct.Trim()),
            StringComparer.OrdinalIgnoreCase);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(webFetch.RequestTimeoutSeconds));
        var effectiveToken = timeoutCts.Token;

        var currentUri = url;
        var redirectCount = 0;

        try
        {
            while (true)
            {
                await SsrfUrlGuard.ValidateResolvedAddressesAsync(
                    currentUri,
                    dnsResolver,
                    isRedirect: redirectCount > 0,
                    effectiveToken);

                using var request = CreateRequest(currentUri, webFetch.UserAgent);
                using var response = await httpTransport.SendAsync(request, effectiveToken);

                if (RedirectStatusCodes.Contains(response.StatusCode))
                {
                    redirectCount++;
                    if (redirectCount > webFetch.MaxRedirects)
                    {
                        throw new UnprocessableEntityAppException(
                            "The web page redirected too many times and could not be imported.",
                            "URL_TOO_MANY_REDIRECTS");
                    }

                    var location = response.Headers.Location;
                    if (location is null)
                    {
                        throw new UnprocessableEntityAppException(
                            "The web page returned an invalid redirect response.",
                            "URL_REDIRECT_BLOCKED");
                    }

                    if (!Uri.TryCreate(currentUri, location, out var nextUri))
                    {
                        throw new UnprocessableEntityAppException(
                            "The web page redirected to a malformed destination.",
                            "URL_REDIRECT_BLOCKED");
                    }

                    currentUri = nextUri;
                    continue;
                }

                ValidateHttpStatus(response.StatusCode);

                var mediaType = ValidateContentType(response.Content.Headers.ContentType, allowedContentTypes);

                var htmlBytes = await ReadBoundedBytesAsync(
                    response.Content,
                    webFetch.MaxResponseBytes,
                    effectiveToken);

                var encoding = ResolveEncoding(response.Content.Headers.ContentType);
                var html = encoding.GetString(htmlBytes);

                var extracted = htmlArticleExtractor.Extract(html, currentUri);
                if (string.IsNullOrWhiteSpace(extracted.Text))
                {
                    throw new UnprocessableEntityAppException(
                        "No readable article content could be extracted from this web page.",
                        "ARTICLE_EXTRACTION_EMPTY");
                }

                logger.LogInformation(
                    "Web article fetched and extracted successfully. Host={Host} Redirects={Redirects} Bytes={Bytes} ExtractedLength={ExtractedLength}",
                    currentUri.Host,
                    redirectCount,
                    htmlBytes.Length,
                    extracted.Text.Length);

                return new WebFetchedArticle(
                    currentUri.AbsoluteUri,
                    extracted.Title,
                    extracted.Text,
                    mediaType);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && timeoutCts.IsCancellationRequested)
        {
            logger.LogWarning("Web fetch timed out for Host={Host}", currentUri.Host);
            throw new GatewayTimeoutAppException(
                "The target web page took too long to respond. Please try again later.",
                "URL_TIMEOUT");
        }
        catch (AppException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Outbound HTTP request failed for Host={Host}", currentUri.Host);
            throw new ExternalDependencyAppException(
                "Unable to retrieve content from the target web page.",
                "URL_FETCH_FAILED");
        }
    }

    private static HttpRequestMessage CreateRequest(Uri uri, string userAgent)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html", 1.0));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xhtml+xml", 0.9));
        return request;
    }

    private static void ValidateHttpStatus(HttpStatusCode statusCode)
    {
        if ((int)statusCode is >= 200 and <= 299)
        {
            return;
        }

        if (statusCode == HttpStatusCode.NotFound || statusCode == HttpStatusCode.Gone)
        {
            throw new UnprocessableEntityAppException(
                "The requested web page was not found (404).",
                "URL_NOT_FOUND");
        }

        if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
        {
            throw new UnprocessableEntityAppException(
                "This web page restricts access or requires login and cannot be imported.",
                "URL_ACCESS_DENIED");
        }

        if (statusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ExternalDependencyAppException(
                "The target website temporarily rate-limited the request. Please try again later.",
                "URL_RATE_LIMITED");
        }

        throw new ExternalDependencyAppException(
            $"The target website returned an unsuccessful status ({(int)statusCode}).",
            "URL_FETCH_FAILED");
    }

    private static string ValidateContentType(
        MediaTypeHeaderValue? contentType,
        HashSet<string> allowedContentTypes)
    {
        var mediaType = contentType?.MediaType?.Trim();
        if (string.IsNullOrWhiteSpace(mediaType) || !allowedContentTypes.Contains(mediaType))
        {
            throw new UnprocessableEntityAppException(
                "This URL does not point to a supported HTML article page.",
                "URL_UNSUPPORTED_CONTENT_TYPE");
        }

        return mediaType;
    }

    private static async Task<byte[]> ReadBoundedBytesAsync(
        HttpContent content,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long declaredLength && declaredLength > maxBytes)
        {
            throw new PayloadTooLargeAppException(
                $"The web page exceeds the maximum supported size of {maxBytes / (1024 * 1024.0):0.#} MB.",
                "URL_RESPONSE_TOO_LARGE");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        var totalRead = 0;

        while (true)
        {
            var bytesRead = await stream.ReadAsync(buffer, cancellationToken);
            if (bytesRead == 0)
            {
                break;
            }

            totalRead += bytesRead;
            if (totalRead > maxBytes)
            {
                throw new PayloadTooLargeAppException(
                    $"The web page exceeds the maximum supported size of {maxBytes / (1024 * 1024.0):0.#} MB.",
                    "URL_RESPONSE_TOO_LARGE");
            }

            memory.Write(buffer, 0, bytesRead);
        }

        return memory.ToArray();
    }

    private static Encoding ResolveEncoding(MediaTypeHeaderValue? contentType)
    {
        var charSet = contentType?.CharSet?.Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(charSet))
        {
            try
            {
                return Encoding.GetEncoding(charSet);
            }
            catch (ArgumentException)
            {
                // Fall back to UTF-8 if upstream sends an invalid charset label.
            }
        }

        return Encoding.UTF8;
    }
}
