using EduScroll.Domain.Exceptions;

namespace EduScroll.Domain.Entities;

/// <summary>
/// Represents a validated AI content analysis for a single user-owned <see cref="Source"/>.
/// Created only after AI output passes strict schema and safety validation.
/// </summary>
public sealed class SourceAnalysis
{
    public const int MaxSubjectLength = 160;
    public const int MaxSummaryLength = 2000;
    public const int MaxProviderNameLength = 64;
    public const int MaxModelNameLength = 128;
    public const int MaxPromptVersionLength = 32;
    public const int MaxConceptLength = 120;
    public const int MaxKeywordLength = 80;
    public const int MaxTopics = 8;
    public const int MaxSubtopicsPerTopic = 6;
    public const int MaxConcepts = 15;
    public const int MaxKeywords = 20;
    public const int MaxSearchQueries = 10;

    private readonly List<SourceTopic> _topics = [];
    private readonly List<SourceSearchQuery> _searchQueries = [];

    private SourceAnalysis()
    {
    }

    public Guid Id { get; private set; }

    public Guid SourceId { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public TargetFeedLanguage TargetFeedLanguage { get; private set; }

    public string Subject { get; private set; } = string.Empty;

    public string Summary { get; private set; } = string.Empty;

    public IReadOnlyList<string> Concepts { get; private set; } = [];

    public IReadOnlyList<string> Keywords { get; private set; } = [];

    public string ProviderName { get; private set; } = string.Empty;

    public string ModelName { get; private set; } = string.Empty;

    public string PromptVersion { get; private set; } = string.Empty;

    public DateTimeOffset AnalyzedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<SourceTopic> Topics => _topics.AsReadOnly();

    public IReadOnlyCollection<SourceSearchQuery> SearchQueries => _searchQueries.AsReadOnly();

    public bool IsOwnedBy(Guid userId) => userId != Guid.Empty && OwnerUserId == userId;

    public static SourceAnalysis Create(
        Guid sourceId,
        Guid ownerUserId,
        TargetFeedLanguage targetFeedLanguage,
        string subject,
        string summary,
        IReadOnlyList<(string TopicName, IReadOnlyList<string> Subtopics)> topics,
        IReadOnlyList<string> concepts,
        IReadOnlyList<string> keywords,
        IReadOnlyList<(string QueryText, string Language)> searchQueries,
        string providerName,
        string modelName,
        string promptVersion,
        DateTimeOffset utcNow)
    {
        if (sourceId == Guid.Empty)
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_ANALYSIS_INVALID_SOURCE",
                "Analysis must be associated with a valid source identifier.");
        }

        if (ownerUserId == Guid.Empty)
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_ANALYSIS_INVALID_OWNER",
                "Analysis must be associated with a valid owner user identifier.");
        }

        if (!Enum.IsDefined(targetFeedLanguage))
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_ANALYSIS_INVALID_TARGET_LANGUAGE",
                "Target feed language is invalid.");
        }

        var normalizedSubject = ValidateRequiredString(
            subject,
            MaxSubjectLength,
            "SOURCE_ANALYSIS_SUBJECT_REQUIRED",
            "SOURCE_ANALYSIS_SUBJECT_TOO_LONG",
            "Subject");

        var normalizedSummary = ValidateRequiredString(
            summary,
            MaxSummaryLength,
            "SOURCE_ANALYSIS_SUMMARY_REQUIRED",
            "SOURCE_ANALYSIS_SUMMARY_TOO_LONG",
            "Summary");

        var normalizedProvider = ValidateRequiredString(
            providerName,
            MaxProviderNameLength,
            "SOURCE_ANALYSIS_PROVIDER_REQUIRED",
            "SOURCE_ANALYSIS_PROVIDER_TOO_LONG",
            "Provider name");

        var normalizedModel = ValidateRequiredString(
            modelName,
            MaxModelNameLength,
            "SOURCE_ANALYSIS_MODEL_REQUIRED",
            "SOURCE_ANALYSIS_MODEL_TOO_LONG",
            "Model name");

        var normalizedPromptVersion = ValidateRequiredString(
            promptVersion,
            MaxPromptVersionLength,
            "SOURCE_ANALYSIS_PROMPT_VERSION_REQUIRED",
            "SOURCE_ANALYSIS_PROMPT_VERSION_TOO_LONG",
            "Prompt version");

        var normalizedConcepts = ValidateStringList(
            concepts,
            MaxConcepts,
            MaxConceptLength,
            "SOURCE_ANALYSIS_CONCEPTS_REQUIRED",
            "SOURCE_ANALYSIS_CONCEPTS_INVALID",
            "Concepts");

        var normalizedKeywords = ValidateStringList(
            keywords,
            MaxKeywords,
            MaxKeywordLength,
            "SOURCE_ANALYSIS_KEYWORDS_REQUIRED",
            "SOURCE_ANALYSIS_KEYWORDS_INVALID",
            "Keywords");

        var normalizedQueries = ValidateSearchQueries(searchQueries, targetFeedLanguage);

        if (topics is null || topics.Count == 0)
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_ANALYSIS_TOPICS_REQUIRED",
                "At least one topic is required.");
        }

        if (topics.Count > MaxTopics)
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_ANALYSIS_TOPICS_INVALID",
                $"Analysis must not contain more than {MaxTopics} top-level topics.");
        }

        var analysis = new SourceAnalysis
        {
            Id = Guid.NewGuid(),
            SourceId = sourceId,
            OwnerUserId = ownerUserId,
            TargetFeedLanguage = targetFeedLanguage,
            Subject = normalizedSubject,
            Summary = normalizedSummary,
            Concepts = normalizedConcepts,
            Keywords = normalizedKeywords,
            ProviderName = normalizedProvider,
            ModelName = normalizedModel,
            PromptVersion = normalizedPromptVersion,
            AnalyzedAtUtc = utcNow,
            UpdatedAtUtc = utcNow
        };

        var seenTopics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var order = 0;

        foreach (var (topicName, subtopics) in topics)
        {
            if (string.IsNullOrWhiteSpace(topicName))
            {
                continue;
            }

            var trimmedTopic = topicName.Trim();
            if (!seenTopics.Add(trimmedTopic))
            {
                continue;
            }

            var parentTopic = SourceTopic.Create(analysis.Id, trimmedTopic, parentTopicId: null, order++);
            analysis._topics.Add(parentTopic);

            if (subtopics is null || subtopics.Count == 0)
            {
                continue;
            }

            if (subtopics.Count > MaxSubtopicsPerTopic)
            {
                throw new InvalidSourceAnalysisDomainException(
                    "SOURCE_ANALYSIS_SUBTOPICS_INVALID",
                    $"A topic must not contain more than {MaxSubtopicsPerTopic} subtopics.");
            }

            var seenSubtopics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var subtopic in subtopics)
            {
                if (string.IsNullOrWhiteSpace(subtopic))
                {
                    continue;
                }

                var trimmedSubtopic = subtopic.Trim();
                if (string.Equals(trimmedSubtopic, trimmedTopic, StringComparison.OrdinalIgnoreCase) ||
                    !seenSubtopics.Add(trimmedSubtopic))
                {
                    continue;
                }

                analysis._topics.Add(
                    SourceTopic.Create(analysis.Id, trimmedSubtopic, parentTopic.Id, order++));
            }
        }

        if (analysis._topics.Count == 0)
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_ANALYSIS_TOPICS_REQUIRED",
                "At least one valid topic is required.");
        }

        for (var i = 0; i < normalizedQueries.Count; i++)
        {
            analysis._searchQueries.Add(
                SourceSearchQuery.Create(
                    analysis.Id,
                    normalizedQueries[i].QueryText,
                    normalizedQueries[i].Language,
                    i));
        }

        return analysis;
    }

    private static List<(string QueryText, string Language)> ValidateSearchQueries(
        IReadOnlyList<(string QueryText, string Language)>? searchQueries,
        TargetFeedLanguage targetFeedLanguage)
    {
        if (searchQueries is null || searchQueries.Count == 0)
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_ANALYSIS_QUERIES_REQUIRED",
                "At least one search query is required.");
        }

        var result = new List<(string QueryText, string Language)>(searchQueries.Count);
        var seen = new HashSet<(string Language, string Query)>(new QueryKeyComparer());
        var enCount = 0;
        var arCount = 0;

        foreach (var (rawQuery, rawLang) in searchQueries)
        {
            if (string.IsNullOrWhiteSpace(rawQuery))
            {
                continue;
            }

            var trimmedQuery = rawQuery.Trim();
            if (trimmedQuery.Length > SourceSearchQuery.MaxQueryTextLength)
            {
                throw new InvalidSourceAnalysisDomainException(
                    "SOURCE_ANALYSIS_QUERIES_INVALID",
                    $"Search query must not exceed {SourceSearchQuery.MaxQueryTextLength} characters.");
            }

            if (string.IsNullOrWhiteSpace(rawLang))
            {
                throw new InvalidSourceAnalysisDomainException(
                    "SOURCE_QUERY_LANGUAGE_REQUIRED",
                    "Search query language is required.");
            }

            var lang = rawLang.Trim().ToLowerInvariant();
            if (lang is not (SourceSearchQuery.EnglishCode or SourceSearchQuery.ArabicCode))
            {
                throw new InvalidSourceAnalysisDomainException(
                    "SOURCE_QUERY_INVALID_LANGUAGE",
                    "Search query language must be 'en' or 'ar'.");
            }

            if ((targetFeedLanguage == TargetFeedLanguage.En && lang != SourceSearchQuery.EnglishCode) ||
                (targetFeedLanguage == TargetFeedLanguage.Ar && lang != SourceSearchQuery.ArabicCode))
            {
                throw new InvalidSourceAnalysisDomainException(
                    "SOURCE_QUERY_LANGUAGE_MISMATCH",
                    "Search query language does not match the target feed language.");
            }

            if (seen.Add((lang, trimmedQuery)))
            {
                result.Add((trimmedQuery, lang));
                if (lang == SourceSearchQuery.EnglishCode) enCount++;
                else arCount++;
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_ANALYSIS_QUERIES_REQUIRED",
                "At least one valid search query is required.");
        }

        if (result.Count > MaxSearchQueries)
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_ANALYSIS_QUERIES_INVALID",
                $"Search queries must not exceed {MaxSearchQueries} items.");
        }

        if (targetFeedLanguage == TargetFeedLanguage.Both && (enCount == 0 || arCount == 0))
        {
            throw new InvalidSourceAnalysisDomainException(
                "SOURCE_QUERY_LANGUAGE_MISMATCH",
                "Both English ('en') and Arabic ('ar') search queries are required when TargetFeedLanguage is 'Both'.");
        }

        return result;
    }

    private static string ValidateRequiredString(
        string? value,
        int maxLength,
        string requiredCode,
        string tooLongCode,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidSourceAnalysisDomainException(requiredCode, $"{label} is required.");
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new InvalidSourceAnalysisDomainException(
                tooLongCode,
                $"{label} must not exceed {maxLength} characters.");
        }

        return trimmed;
    }

    private static string[] ValidateStringList(
        IReadOnlyList<string>? values,
        int maxCount,
        int maxItemLength,
        string requiredCode,
        string invalidCode,
        string label)
    {
        if (values is null || values.Count == 0)
        {
            throw new InvalidSourceAnalysisDomainException(
                requiredCode,
                $"At least one entry is required for {label}.");
        }

        var result = new List<string>(values.Count);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in values)
        {
            if (string.IsNullOrWhiteSpace(item))
            {
                continue;
            }

            var trimmed = item.Trim();
            if (trimmed.Length > maxItemLength)
            {
                throw new InvalidSourceAnalysisDomainException(
                    invalidCode,
                    $"Items in {label} must not exceed {maxItemLength} characters.");
            }

            if (seen.Add(trimmed))
            {
                result.Add(trimmed);
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidSourceAnalysisDomainException(
                requiredCode,
                $"At least one valid entry is required for {label}.");
        }

        if (result.Count > maxCount)
        {
            throw new InvalidSourceAnalysisDomainException(
                invalidCode,
                $"{label} must not exceed {maxCount} items.");
        }

        return [.. result];
    }

    private sealed class QueryKeyComparer : IEqualityComparer<(string Language, string Query)>
    {
        public bool Equals((string Language, string Query) x, (string Language, string Query) y) =>
            string.Equals(x.Language, y.Language, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Query, y.Query, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Language, string Query) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Language),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Query));
    }
}
