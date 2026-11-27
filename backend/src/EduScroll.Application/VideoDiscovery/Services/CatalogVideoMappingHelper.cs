using EduScroll.Application.VideoDiscovery.Contracts;
using EduScroll.Domain.Entities;

namespace EduScroll.Application.VideoDiscovery.Services;

/// <summary>
/// Shared mapping, truncation, URL validation, and language normalization helpers
/// for converting RSS, fallback search, and metadata records into catalog entities and DTOs.
/// </summary>
internal static class CatalogVideoMappingHelper
{
    public static VideoCandidateDto MapToCandidateDto(CatalogVideo video)
    {
        ArgumentNullException.ThrowIfNull(video);

        return new VideoCandidateDto(
            Id: video.Id,
            YouTubeVideoId: video.YouTubeVideoId,
            WhitelistedChannelId: video.WhitelistedChannelId,
            ChannelExternalId: video.ChannelExternalId,
            ChannelName: video.ChannelName,
            Title: video.Title,
            Description: video.Description,
            ThumbnailUrl: video.ThumbnailUrl,
            Language: video.Language,
            DurationSeconds: video.DurationSeconds,
            ViewCount: video.ViewCount,
            LikeCount: video.LikeCount,
            PublishedAtUtc: video.PublishedAtUtc,
            DiscoverySource: video.DiscoverySource.ToString(),
            WatchUrl: $"https://www.youtube.com/watch?v={Uri.EscapeDataString(video.YouTubeVideoId)}");
    }

    public static string ResolveRequiredText(
        string? preferred,
        string? fallback,
        string? secondaryFallback,
        int maxLength)
    {
        var candidate = !string.IsNullOrWhiteSpace(preferred)
            ? preferred
            : !string.IsNullOrWhiteSpace(fallback)
                ? fallback
                : secondaryFallback;

        if (string.IsNullOrWhiteSpace(candidate))
        {
            return string.Empty;
        }

        var trimmed = candidate.Trim();
        return TruncateSurrogateSafe(trimmed, maxLength);
    }

    public static string TruncateSurrogateSafe(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var length = char.IsHighSurrogate(value[maxLength - 1])
            ? maxLength - 1
            : maxLength;

        return value[..length].TrimEnd();
    }

    public static string? ResolveThumbnailUrl(string? preferredUrl, string? fallbackUrl)
    {
        if (TryNormalizeThumbnailUrl(preferredUrl, out var validPreferred))
        {
            return validPreferred;
        }

        if (TryNormalizeThumbnailUrl(fallbackUrl, out var validFallback))
        {
            return validFallback;
        }

        return null;
    }

    public static string ResolveVideoLanguage(string? preferredLanguage, string fallbackLanguage)
    {
        if (!string.IsNullOrWhiteSpace(preferredLanguage))
        {
            var trimmed = preferredLanguage.Trim();
            if (trimmed.StartsWith(SourceSearchQuery.EnglishCode, StringComparison.OrdinalIgnoreCase))
            {
                return SourceSearchQuery.EnglishCode;
            }

            if (trimmed.StartsWith(SourceSearchQuery.ArabicCode, StringComparison.OrdinalIgnoreCase))
            {
                return SourceSearchQuery.ArabicCode;
            }
        }

        return fallbackLanguage;
    }

    private static bool TryNormalizeThumbnailUrl(string? rawUrl, out string? normalizedUrl)
    {
        if (!string.IsNullOrWhiteSpace(rawUrl))
        {
            var trimmed = rawUrl.Trim();
            if (trimmed.Length <= CatalogVideo.MaxThumbnailUrlLength &&
                Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
                !string.IsNullOrWhiteSpace(uri.Host))
            {
                normalizedUrl = trimmed;
                return true;
            }
        }

        normalizedUrl = null;
        return false;
    }
}
