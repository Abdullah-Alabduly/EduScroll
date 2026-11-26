using EduScroll.Domain.Entities;

namespace EduScroll.Infrastructure.Configuration;

public sealed class SourceIngestionOptions
{
    public const string SectionName = "SourceIngestion";

    public int MinExtractedTextLength { get; set; } = Source.MinExtractedTextLength;

    public int MaxExtractedTextLength { get; set; } = Source.DefaultMaxExtractedTextLength;

    public int PreviewMaxLength { get; set; } = 240;

    public int DefaultPageSize { get; set; } = 20;

    public int MaxPageSize { get; set; } = 50;

    public WebFetchOptions WebFetch { get; set; } = new();
}

public sealed class WebFetchOptions
{
    public int ConnectTimeoutSeconds { get; set; } = 10;

    public int RequestTimeoutSeconds { get; set; } = 20;

    public int MaxResponseBytes { get; set; } = 2 * 1024 * 1024; // 2 MB

    public int MaxRedirects { get; set; } = 3;

    public string UserAgent { get; set; } = "EduScrollBot/1.0 (+https://eduscroll.app)";

    public string[] AllowedContentTypes { get; set; } =
    [
        "text/html",
        "application/xhtml+xml"
    ];
}
