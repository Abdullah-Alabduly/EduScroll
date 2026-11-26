using EduScroll.Domain.Exceptions;

namespace EduScroll.Domain.Entities;

/// <summary>
/// Represents a language-tagged, sanitized educational search query derived from a validated <see cref="SourceAnalysis"/>.
/// </summary>
public sealed class SourceSearchQuery
{
    public const int MaxQueryTextLength = 150;
    public const int MaxLanguageLength = 8;
    public const string EnglishCode = "en";
    public const string ArabicCode = "ar";

    private SourceSearchQuery()
    {
    }

    public Guid Id { get; private set; }

    public Guid SourceAnalysisId { get; private set; }

    public string QueryText { get; private set; } = string.Empty;

    public string Language { get; private set; } = string.Empty;

    public int DisplayOrder { get; private set; }

    public static bool IsSupportedLanguageCode(string? code) =>
        string.Equals(code, EnglishCode, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(code, ArabicCode, StringComparison.OrdinalIgnoreCase);

    internal static SourceSearchQuery Create(
        Guid sourceAnalysisId,
        string queryText,
        string language,
        int displayOrder)
    {
        if (sourceAnalysisId == Guid.Empty)
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_QUERY_INVALID_ANALYSIS",
                "Search query must belong to a valid analysis.");
        }

        if (string.IsNullOrWhiteSpace(queryText))
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_QUERY_REQUIRED",
                "Search query text must not be empty.");
        }

        var trimmedQuery = queryText.Trim();
        if (trimmedQuery.Length > MaxQueryTextLength)
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_QUERY_TOO_LONG",
                $"Search query text must not exceed {MaxQueryTextLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(language))
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_QUERY_LANGUAGE_REQUIRED",
                "Search query language is required.");
        }

        var normalizedLanguage = language.Trim().ToLowerInvariant();
        if (normalizedLanguage is not (EnglishCode or ArabicCode))
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_QUERY_INVALID_LANGUAGE",
                "Search query language must be 'en' or 'ar'.");
        }

        if (displayOrder < 0)
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_QUERY_INVALID_ORDER",
                "Search query display order must be non-negative.");
        }

        return new SourceSearchQuery
        {
            Id = Guid.NewGuid(),
            SourceAnalysisId = sourceAnalysisId,
            QueryText = trimmedQuery,
            Language = normalizedLanguage,
            DisplayOrder = displayOrder
        };
    }
}
