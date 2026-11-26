using EduScroll.Application.Sources.Models;

namespace EduScroll.Application.Sources.Ports;

/// <summary>
/// Securely fetches and extracts readable article text from a user-supplied HTTP/HTTPS URL.
/// Implementations must enforce SSRF, redirect, timeout, content-type, and payload-size controls.
/// </summary>
public interface IWebContentFetcher
{
    Task<WebFetchedArticle> FetchAndExtractAsync(
        Uri url,
        CancellationToken cancellationToken);
}
