using EduScroll.Domain.Exceptions;

namespace EduScroll.Domain.Entities;

/// <summary>
/// Represents cached YouTube video metadata in the EduScroll discovery catalog.
/// Stores metadata and references ONLY — never video media files (BR-001).
/// </summary>
public sealed class CatalogVideo
{
    public const int MaxYouTubeVideoIdLength = 32;
    public const int MaxChannelExternalIdLength = 64;
    public const int MaxChannelNameLength = 200;
    public const int MaxTitleLength = 300;
    public const int MaxDescriptionLength = 5000;
    public const int MaxThumbnailUrlLength = 1024;
    public const int MaxLanguageLength = 8;

    private CatalogVideo()
    {
    }

    public Guid Id { get; private set; }

    public string YouTubeVideoId { get; private set; } = string.Empty;

    public Guid? WhitelistedChannelId { get; private set; }

    public string ChannelExternalId { get; private set; } = string.Empty;

    public string ChannelName { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public string? ThumbnailUrl { get; private set; }

    public string Language { get; private set; } = string.Empty;

    public int? DurationSeconds { get; private set; }

    public long ViewCount { get; private set; }

    public long LikeCount { get; private set; }

    public DateTimeOffset PublishedAtUtc { get; private set; }

    public DiscoverySource DiscoverySource { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static CatalogVideo Create(
        string youTubeVideoId,
        Guid? whitelistedChannelId,
        string channelExternalId,
        string channelName,
        string title,
        string? description,
        string? thumbnailUrl,
        string language,
        int? durationSeconds,
        long viewCount,
        long likeCount,
        DateTimeOffset publishedAtUtc,
        DiscoverySource discoverySource,
        DateTimeOffset utcNow)
    {
        var normalizedVideoId = ValidateRequired(
            youTubeVideoId,
            MaxYouTubeVideoIdLength,
            "VIDEO_ID_REQUIRED",
            "VIDEO_ID_TOO_LONG",
            "YouTube video ID");

        if (whitelistedChannelId.HasValue && whitelistedChannelId.Value == Guid.Empty)
        {
            throw new InvalidVideoCatalogDomainException(
                "VIDEO_INVALID_WHITELISTED_CHANNEL",
                "Whitelisted channel ID cannot be empty when specified.");
        }

        var normalizedChannelExtId = ValidateRequired(
            channelExternalId,
            MaxChannelExternalIdLength,
            "VIDEO_CHANNEL_ID_REQUIRED",
            "VIDEO_CHANNEL_ID_TOO_LONG",
            "Channel external ID");

        var normalizedChannelName = ValidateRequired(
            channelName,
            MaxChannelNameLength,
            "VIDEO_CHANNEL_NAME_REQUIRED",
            "VIDEO_CHANNEL_NAME_TOO_LONG",
            "Channel name");

        var normalizedTitle = ValidateRequired(
            title,
            MaxTitleLength,
            "VIDEO_TITLE_REQUIRED",
            "VIDEO_TITLE_TOO_LONG",
            "Video title");

        var normalizedDescription = NormalizeDescription(description);
        var normalizedThumbnail = ValidateThumbnailUrl(thumbnailUrl);
        var normalizedLanguage = ValidateLanguage(language);
        ValidateNumericMetrics(durationSeconds, viewCount, likeCount);

        if (!Enum.IsDefined(discoverySource))
        {
            throw new InvalidVideoCatalogDomainException(
                "VIDEO_DISCOVERY_SOURCE_INVALID",
                "Discovery source is invalid.");
        }

        return new CatalogVideo
        {
            Id = Guid.NewGuid(),
            YouTubeVideoId = normalizedVideoId,
            WhitelistedChannelId = whitelistedChannelId,
            ChannelExternalId = normalizedChannelExtId,
            ChannelName = normalizedChannelName,
            Title = normalizedTitle,
            Description = normalizedDescription,
            ThumbnailUrl = normalizedThumbnail,
            Language = normalizedLanguage,
            DurationSeconds = durationSeconds,
            ViewCount = viewCount,
            LikeCount = likeCount,
            PublishedAtUtc = publishedAtUtc,
            DiscoverySource = discoverySource,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow
        };
    }

    public void UpdateFromMetadata(
        Guid? whitelistedChannelId,
        string channelExternalId,
        string channelName,
        string title,
        string? description,
        string? thumbnailUrl,
        string language,
        int? durationSeconds,
        long viewCount,
        long likeCount,
        DateTimeOffset publishedAtUtc,
        DiscoverySource discoverySource,
        DateTimeOffset updatedAtUtc)
    {
        if (whitelistedChannelId.HasValue && whitelistedChannelId.Value == Guid.Empty)
        {
            throw new InvalidVideoCatalogDomainException(
                "VIDEO_INVALID_WHITELISTED_CHANNEL",
                "Whitelisted channel ID cannot be empty when specified.");
        }

        ChannelExternalId = ValidateRequired(
            channelExternalId,
            MaxChannelExternalIdLength,
            "VIDEO_CHANNEL_ID_REQUIRED",
            "VIDEO_CHANNEL_ID_TOO_LONG",
            "Channel external ID");

        ChannelName = ValidateRequired(
            channelName,
            MaxChannelNameLength,
            "VIDEO_CHANNEL_NAME_REQUIRED",
            "VIDEO_CHANNEL_NAME_TOO_LONG",
            "Channel name");

        Title = ValidateRequired(
            title,
            MaxTitleLength,
            "VIDEO_TITLE_REQUIRED",
            "VIDEO_TITLE_TOO_LONG",
            "Video title");

        Description = NormalizeDescription(description);
        ThumbnailUrl = ValidateThumbnailUrl(thumbnailUrl);
        Language = ValidateLanguage(language);
        ValidateNumericMetrics(durationSeconds, viewCount, likeCount);

        if (!Enum.IsDefined(discoverySource))
        {
            throw new InvalidVideoCatalogDomainException(
                "VIDEO_DISCOVERY_SOURCE_INVALID",
                "Discovery source is invalid.");
        }

        // Preserve existing WhitelistedChannelId link if incoming update has null.
        if (whitelistedChannelId.HasValue)
        {
            WhitelistedChannelId = whitelistedChannelId;
        }

        // Preserve known duration if an RSS refresh omits it.
        if (durationSeconds.HasValue)
        {
            DurationSeconds = durationSeconds;
        }

        ViewCount = Math.Max(ViewCount, viewCount);
        LikeCount = Math.Max(LikeCount, likeCount);
        PublishedAtUtc = publishedAtUtc;

        // Upgrade provenance to WhitelistRss if matched to a whitelisted channel.
        if (DiscoverySource != DiscoverySource.WhitelistRss)
        {
            DiscoverySource = discoverySource;
        }

        UpdatedAtUtc = updatedAtUtc;
    }

    private static string ValidateRequired(
        string? value,
        int maxLength,
        string requiredCode,
        string tooLongCode,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidVideoCatalogDomainException(requiredCode, $"{label} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new InvalidVideoCatalogDomainException(
                tooLongCode,
                $"{label} must not exceed {maxLength} characters.");
        }

        return trimmed;
    }

    private static string NormalizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var trimmed = description.Trim();
        return trimmed.Length > MaxDescriptionLength
            ? trimmed[..MaxDescriptionLength]
            : trimmed;
    }

    private static string? ValidateThumbnailUrl(string? thumbnailUrl)
    {
        if (string.IsNullOrWhiteSpace(thumbnailUrl))
        {
            return null;
        }

        var trimmed = thumbnailUrl.Trim();
        if (trimmed.Length > MaxThumbnailUrlLength)
        {
            throw new InvalidVideoCatalogDomainException(
                "VIDEO_THUMBNAIL_URL_TOO_LONG",
                $"Thumbnail URL must not exceed {MaxThumbnailUrlLength} characters.");
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new InvalidVideoCatalogDomainException(
                "VIDEO_THUMBNAIL_URL_INVALID",
                "Thumbnail URL must be a valid absolute HTTP or HTTPS URL.");
        }

        return trimmed;
    }

    private static string ValidateLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            throw new InvalidVideoCatalogDomainException(
                "VIDEO_LANGUAGE_REQUIRED",
                "Video language is required.");
        }

        var normalized = language.Trim().ToLowerInvariant();
        if (normalized is not (SourceSearchQuery.EnglishCode or SourceSearchQuery.ArabicCode))
        {
            throw new InvalidVideoCatalogDomainException(
                "VIDEO_LANGUAGE_INVALID",
                "Video language must be 'en' or 'ar'.");
        }

        return normalized;
    }

    private static void ValidateNumericMetrics(int? durationSeconds, long viewCount, long likeCount)
    {
        if (durationSeconds.HasValue && durationSeconds.Value < 0)
        {
            throw new InvalidVideoCatalogDomainException(
                "VIDEO_DURATION_NEGATIVE",
                "Video duration cannot be negative.");
        }

        if (viewCount < 0)
        {
            throw new InvalidVideoCatalogDomainException(
                "VIDEO_VIEW_COUNT_NEGATIVE",
                "Video view count cannot be negative.");
        }

        if (likeCount < 0)
        {
            throw new InvalidVideoCatalogDomainException(
                "VIDEO_LIKE_COUNT_NEGATIVE",
                "Video like count cannot be negative.");
        }
    }
}
