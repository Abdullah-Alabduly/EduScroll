namespace EduScroll.Infrastructure.Configuration;

public sealed class YouTubeOptions
{
    public const string SectionName = "YouTube";

    public const int MaxAllowedMetadataBatchSize = 50;

    public const int MaxAllowedVideosPerChannelPerSync = 50;

    public string ApiBaseUrl { get; set; } = "https://www.googleapis.com/youtube/v3";

    public string RssBaseUrl { get; set; } = "https://www.youtube.com/feeds/videos.xml";

    /// <summary>
    /// Optional YouTube Data API v3 key for metadata enrichment and search fallback.
    /// Never commit real keys to appsettings*.json; inject via User Secrets or environment variables.
    /// </summary>
    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 15;

    public int MaxRetries { get; set; } = 2;

    public int RetryBaseDelayMilliseconds { get; set; } = 500;

    public int MetadataBatchSize { get; set; } = 50;

    public int MaxVideosPerChannelPerSync { get; set; } = 15;
}
