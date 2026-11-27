namespace EduScroll.Application.VideoDiscovery.Contracts;

public sealed record CreateWhitelistedChannelRequest(
    string YouTubeChannelId,
    string Name,
    string Language,
    string TopicCategory,
    double TrustScore = 0.85,
    bool IsActive = true);
