namespace EduScroll.Application.AiAnalysis.Contracts;

public sealed record AnalyzeSourceRequest(
    bool ForceRefresh = false,
    string? LanguageHint = null,
    string? TargetFeedLanguage = null);
