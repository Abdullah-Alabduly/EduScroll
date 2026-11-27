namespace EduScroll.Application.VideoDiscovery.Contracts;

public sealed record DiscoverSourceVideosRequest(
    int? MaxCandidates = null);
