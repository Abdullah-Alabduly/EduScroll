using EduScroll.Domain.Entities;

namespace EduScroll.Application.VideoDiscovery.Contracts;

public sealed record VideoDiscoveryQueryInput(
    string Query,
    string Language);

public sealed record VideoDiscoveryRequest(
    IReadOnlyList<VideoDiscoveryQueryInput> SearchQueries,
    TargetFeedLanguage TargetFeedLanguage = TargetFeedLanguage.En,
    int DesiredCandidateCount = 20,
    IReadOnlyList<string>? AdditionalKeywords = null);
