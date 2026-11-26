using EduScroll.Domain.Entities;

namespace EduScroll.Application.AiAnalysis.Models;

public sealed record AiAnalysisProviderRequest(
    string SourceTitle,
    string CleanedText,
    TargetFeedLanguage TargetFeedLanguage,
    string? LanguageHint,
    int MaxTopics,
    int MaxSubtopicsPerTopic,
    int MaxConcepts,
    int MaxKeywords,
    int SingleLanguageMinSearchQueries,
    int SingleLanguageMaxSearchQueries,
    int DualLanguageMinSearchQueriesPerLanguage,
    int DualLanguageMaxSearchQueriesPerLanguage,
    string? RepairFeedback = null);

public sealed record AiAnalysisProviderResponse(
    string RawJson,
    string ProviderName,
    string ModelName,
    string PromptVersion);

public sealed record ValidatedAiTopic(
    string Name,
    IReadOnlyList<string> Subtopics);

public sealed record ValidatedAiSearchQuery(
    string Query,
    string Language);

public sealed record ValidatedAiAnalysisOutput(
    string Subject,
    string PrimaryTopic,
    string Summary,
    IReadOnlyList<ValidatedAiTopic> Topics,
    IReadOnlyList<string> Concepts,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<ValidatedAiSearchQuery> SearchQueries);

/// <summary>
/// Application-layer input/output bounds and bounded repair configuration for AI analysis.
/// </summary>
public sealed record AiAnalysisLimits(
    int MinInputCharacters = Source.MinExtractedTextLength,
    int MaxInputCharacters = 24_000,
    int MaxSchemaRepairAttempts = 1,
    int MaxSummaryLength = 1_200,
    int MaxTopics = 5,
    int MaxSubtopicsPerTopic = 4,
    int MaxConcepts = 10,
    int MaxKeywords = 12,
    int SingleLanguageMinSearchQueries = 3,
    int SingleLanguageMaxSearchQueries = 8,
    int DualLanguageMinSearchQueriesPerLanguage = 2,
    int DualLanguageMaxSearchQueriesPerLanguage = 5);
