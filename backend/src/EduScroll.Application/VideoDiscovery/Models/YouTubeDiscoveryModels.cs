namespace EduScroll.Application.VideoDiscovery.Models;

public sealed record YouTubeRssVideoItem(
    string YouTubeVideoId,
    string ChannelExternalId,
    string ChannelName,
    string Title,
    string Description,
    string? ThumbnailUrl,
    long ViewCount,
    DateTimeOffset PublishedAtUtc);

public sealed record YouTubeVideoMetadataItem(
    string YouTubeVideoId,
    string? ChannelExternalId,
    string? ChannelName,
    string? Title,
    string? Description,
    string? ThumbnailUrl,
    string? DefaultLanguage,
    int? DurationSeconds,
    long? ViewCount,
    long? LikeCount,
    DateTimeOffset? PublishedAtUtc);

public sealed record YouTubeSearchFallbackItem(
    string YouTubeVideoId,
    string ChannelExternalId,
    string ChannelName,
    string Title,
    string Description,
    string? ThumbnailUrl,
    string Language,
    DateTimeOffset PublishedAtUtc);

public sealed record VideoDiscoveryLimits(
    int MetadataBatchSize = 50,
    int MaxVideosPerChannelPerSync = 15,
    bool EnableSearchFallback = true,
    int MinLocalCandidatesThreshold = 15,
    int MaxFallbackQueriesPerDiscovery = 2,
    int MaxFallbackResultsPerQuery = 10,
    int MaxVideoDurationSeconds = 240);
