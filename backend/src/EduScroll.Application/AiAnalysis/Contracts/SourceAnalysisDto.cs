namespace EduScroll.Application.AiAnalysis.Contracts;

public sealed record SourceAnalysisTopicDto(
    string Name,
    IReadOnlyList<string> Subtopics);

public sealed record SourceSearchQueryDto(
    string Query,
    string Language);

public sealed record SourceAnalysisDto(
    Guid Id,
    Guid SourceId,
    string TargetFeedLanguage,
    string Subject,
    string Topic,
    string Summary,
    IReadOnlyList<SourceAnalysisTopicDto> Topics,
    IReadOnlyList<string> Concepts,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<SourceSearchQueryDto> SearchQueries,
    string Provider,
    string Model,
    string PromptVersion,
    DateTimeOffset AnalyzedAtUtc,
    DateTimeOffset UpdatedAtUtc);
