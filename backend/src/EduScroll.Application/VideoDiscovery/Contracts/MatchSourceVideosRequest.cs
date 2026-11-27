namespace EduScroll.Application.VideoDiscovery.Contracts;

public sealed record MatchSourceVideosRequest(
    string? TargetFeedLanguage = null,
    int? MaxCandidates = null,
    bool ForceReanalyze = false);
