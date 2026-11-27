namespace EduScroll.Application.VideoDiscovery.Contracts;

public sealed record WhitelistedChannelDto(
    Guid Id,
    string YouTubeChannelId,
    string Name,
    string Language,
    string TopicCategory,
    double TrustScore,
    bool IsActive,
    DateTimeOffset? LastSyncedAtUtc,
    DateTimeOffset CreatedAtUtc);
