using EduScroll.Application.AiAnalysis.Contracts;

namespace EduScroll.Application.VideoDiscovery.Contracts;

public sealed record SourceVideoMatchResultDto(
    Guid SourceId,
    Guid AnalysisId,
    string TargetFeedLanguage,
    string Subject,
    string Topic,
    string Summary,
    IReadOnlyList<SourceSearchQueryDto> SearchQueries,
    string Provider,
    string Model,
    IReadOnlyList<VideoCandidateDto> Candidates,
    int LocalCandidateCount,
    int FallbackCandidateCount,
    bool FallbackTriggered,
    string? FallbackSkippedReason);
