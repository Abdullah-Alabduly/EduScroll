namespace EduScroll.Application.VideoDiscovery.Contracts;

public sealed record VideoDiscoveryResultDto(
    IReadOnlyList<VideoCandidateDto> Candidates,
    int LocalCandidateCount,
    int FallbackCandidateCount,
    bool FallbackTriggered,
    string? FallbackSkippedReason);
