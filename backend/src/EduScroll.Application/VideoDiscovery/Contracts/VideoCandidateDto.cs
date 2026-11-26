namespace EduScroll.Application.VideoDiscovery.Contracts;

/// <summary>
/// Normalized video metadata candidate contract shared across catalog retrieval and discovery steps.
/// Contains metadata references ONLY — never raw video streams (BR-001).
/// </summary>
public sealed record VideoCandidateDto(
    Guid Id,
    string YouTubeVideoId,
    Guid? WhitelistedChannelId,
    string ChannelExternalId,
    string ChannelName,
    string Title,
    string Description,
    string? ThumbnailUrl,
    string Language,
    int? DurationSeconds,
    long ViewCount,
    long LikeCount,
    DateTimeOffset PublishedAtUtc,
    string DiscoverySource);
