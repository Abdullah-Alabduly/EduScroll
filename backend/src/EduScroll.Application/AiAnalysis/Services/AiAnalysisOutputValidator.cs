using System.Text.Json;
using System.Text.RegularExpressions;
using EduScroll.Application.AiAnalysis.Models;
using EduScroll.Application.Sources.Services;
using EduScroll.Domain.Entities;

namespace EduScroll.Application.AiAnalysis.Services;

public sealed record AiOutputValidationResult(
    bool IsValid,
    ValidatedAiAnalysisOutput? Output,
    IReadOnlyList<string> Errors)
{
    public string BuildSafeRepairFeedback() =>
        Errors.Count == 0
            ? "Return a valid JSON object matching the required schema."
            : $"Fix these schema validation errors and return ONLY raw JSON: {string.Join(" ", Errors.Take(5))}";
}

/// <summary>
/// Strictly parses, validates, and sanitizes untrusted AI JSON responses (BR-005, SEC-009, SEC-010).
/// Enforces multi-language feed search query tagging and per-language count bounds.
/// </summary>
public static partial class AiAnalysisOutputValidator
{
    private const int MinSummaryLength = 20;
    private const int MinShortFieldLength = 2;

    private static readonly JsonDocumentOptions StrictJsonOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 16
    };

    private static readonly string[] ForbiddenInjectionMarkers =
    [
        "untrusted_source_text",
        "ignore previous instructions",
        "ignore all previous instructions",
        "disregard previous instructions",
        "reveal your system prompt",
        "system prompt:",
        "javascript:",
        "<script",
        "</script",
        "<iframe",
        "onerror=",
        "onload="
    ];

    [GeneratedRegex(@"<\s*/?\s*[a-zA-Z][a-zA-Z0-9\-]*(\s[^>]*)?>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    public static AiOutputValidationResult Validate(
        string? rawResponse,
        TargetFeedLanguage targetFeedLanguage,
        AiAnalysisLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        if (string.IsNullOrWhiteSpace(rawResponse))
        {
            return Fail("AI response was empty.");
        }

        if (!TryExtractJsonCandidate(rawResponse, out var jsonCandidate))
        {
            return Fail("AI response did not contain a valid JSON object.");
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(jsonCandidate, StrictJsonOptions);
        }
        catch (JsonException)
        {
            return Fail("AI response contained malformed or unparseable JSON.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Fail("AI JSON root must be an object.");
            }

            var root = document.RootElement;
            var errors = new List<string>();

            var subject = ReadRequiredText(
                root,
                ["subject"],
                MinShortFieldLength,
                SourceAnalysis.MaxSubjectLength,
                "subject",
                errors);

            var explicitTopic = ReadOptionalText(
                root,
                ["topic", "primaryTopic", "primary_topic"],
                MinShortFieldLength,
                SourceTopic.MaxNameLength,
                "topic",
                errors);

            var summary = ReadRequiredText(
                root,
                ["summary"],
                MinSummaryLength,
                Math.Min(limits.MaxSummaryLength, SourceAnalysis.MaxSummaryLength),
                "summary",
                errors);

            var topics = ReadTopics(root, explicitTopic, limits, errors);
            var concepts = ReadStringArray(
                root,
                ["concepts"],
                limits.MaxConcepts,
                SourceAnalysis.MaxConceptLength,
                "concepts",
                errors);
            var keywords = ReadStringArray(
                root,
                ["keywords"],
                limits.MaxKeywords,
                SourceAnalysis.MaxKeywordLength,
                "keywords",
                errors);
            var searchQueries = ReadLanguageTaggedSearchQueries(
                root,
                targetFeedLanguage,
                limits,
                errors);

            if (errors.Count > 0 || subject is null || summary is null || topics.Count == 0)
            {
                if (topics.Count == 0 && !errors.Any(e => e.Contains("topic", StringComparison.OrdinalIgnoreCase)))
                {
                    errors.Add("At least one valid topic is required.");
                }

                return new AiOutputValidationResult(false, null, errors);
            }

            var primaryTopic = !string.IsNullOrWhiteSpace(explicitTopic)
                ? explicitTopic
                : topics[0].Name;

            var validated = new ValidatedAiAnalysisOutput(
                subject,
                primaryTopic,
                summary,
                topics,
                concepts,
                keywords,
                searchQueries);

            return new AiOutputValidationResult(true, validated, []);
        }
    }

    private static List<ValidatedAiSearchQuery> ReadLanguageTaggedSearchQueries(
        JsonElement root,
        TargetFeedLanguage targetFeedLanguage,
        AiAnalysisLimits limits,
        List<string> errors)
    {
        var items = new List<ValidatedAiSearchQuery>();

        if (!TryGetPropertyCaseInsensitive(root, ["searchQueries", "search_queries"], out var element))
        {
            errors.Add("Field 'searchQueries' is required and must be an array of { \"query\", \"language\" } objects.");
            return items;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add("Field 'searchQueries' must be an array of { \"query\", \"language\" } objects.");
            return items;
        }

        var seen = new HashSet<(string Language, string Query)>(new QueryKeyComparer());
        var enCount = 0;
        var arCount = 0;

        foreach (var entry in element.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                errors.Add("Every entry in 'searchQueries' must be an object with 'query' and 'language' ('en' or 'ar').");
                continue;
            }

            if (!TryGetPropertyCaseInsensitive(entry, ["query"], out var queryEl) ||
                queryEl.ValueKind != JsonValueKind.String)
            {
                errors.Add("Every entry in 'searchQueries' must include a string 'query'.");
                continue;
            }

            if (!TryGetPropertyCaseInsensitive(entry, ["language"], out var langEl) ||
                langEl.ValueKind != JsonValueKind.String)
            {
                errors.Add("Every entry in 'searchQueries' must include a string 'language' ('en' or 'ar').");
                continue;
            }

            var queryText = SourceTextNormalizer.NormalizeTitle(queryEl.GetString())
                .Trim('"', '\'', '`', ' ');

            if (!ValidateShortItem(queryText, SourceSearchQuery.MaxQueryTextLength, "searchQueries", errors))
            {
                continue;
            }

            var language = (langEl.GetString() ?? string.Empty).Trim().ToLowerInvariant();
            if (language is not (SourceSearchQuery.EnglishCode or SourceSearchQuery.ArabicCode))
            {
                errors.Add($"Unsupported search query language '{language}'. Allowed values are 'en' and 'ar'.");
                continue;
            }

            if (targetFeedLanguage == TargetFeedLanguage.En && language != SourceSearchQuery.EnglishCode)
            {
                errors.Add("All search queries must have language 'en' when targetFeedLanguage is 'en'.");
                continue;
            }

            if (targetFeedLanguage == TargetFeedLanguage.Ar && language != SourceSearchQuery.ArabicCode)
            {
                errors.Add("All search queries must have language 'ar' when targetFeedLanguage is 'ar'.");
                continue;
            }

            if (seen.Add((language, queryText)))
            {
                items.Add(new ValidatedAiSearchQuery(queryText, language));
                if (language == SourceSearchQuery.EnglishCode)
                {
                    enCount++;
                }
                else
                {
                    arCount++;
                }
            }
        }

        ValidateQueryCounts(targetFeedLanguage, enCount, arCount, limits, errors);
        return items;
    }

    private static void ValidateQueryCounts(
        TargetFeedLanguage targetFeedLanguage,
        int enCount,
        int arCount,
        AiAnalysisLimits limits,
        List<string> errors)
    {
        switch (targetFeedLanguage)
        {
            case TargetFeedLanguage.En:
                if (enCount < limits.SingleLanguageMinSearchQueries || enCount > limits.SingleLanguageMaxSearchQueries)
                {
                    errors.Add(
                        $"Expected between {limits.SingleLanguageMinSearchQueries} and {limits.SingleLanguageMaxSearchQueries} English ('en') search queries, received {enCount}.");
                }
                break;

            case TargetFeedLanguage.Ar:
                if (arCount < limits.SingleLanguageMinSearchQueries || arCount > limits.SingleLanguageMaxSearchQueries)
                {
                    errors.Add(
                        $"Expected between {limits.SingleLanguageMinSearchQueries} and {limits.SingleLanguageMaxSearchQueries} Arabic ('ar') search queries, received {arCount}.");
                }
                break;

            case TargetFeedLanguage.Both:
                if (enCount < limits.DualLanguageMinSearchQueriesPerLanguage ||
                    enCount > limits.DualLanguageMaxSearchQueriesPerLanguage)
                {
                    errors.Add(
                        $"Expected between {limits.DualLanguageMinSearchQueriesPerLanguage} and {limits.DualLanguageMaxSearchQueriesPerLanguage} English ('en') search queries for 'both', received {enCount}.");
                }

                if (arCount < limits.DualLanguageMinSearchQueriesPerLanguage ||
                    arCount > limits.DualLanguageMaxSearchQueriesPerLanguage)
                {
                    errors.Add(
                        $"Expected between {limits.DualLanguageMinSearchQueriesPerLanguage} and {limits.DualLanguageMaxSearchQueriesPerLanguage} Arabic ('ar') search queries for 'both', received {arCount}.");
                }
                break;
        }
    }

    private static bool TryExtractJsonCandidate(string raw, out string candidate)
    {
        var trimmed = raw.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine > 0 && lastFence > firstNewLine)
            {
                trimmed = trimmed[(firstNewLine + 1)..lastFence].Trim();
            }
        }

        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            candidate = trimmed;
            return true;
        }

        var firstBrace = trimmed.IndexOf('{');
        var lastBrace = trimmed.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            candidate = trimmed[firstBrace..(lastBrace + 1)].Trim();
            return true;
        }

        candidate = string.Empty;
        return false;
    }

    private static string? ReadRequiredText(
        JsonElement root,
        string[] propertyNames,
        int minLength,
        int maxLength,
        string fieldLabel,
        List<string> errors)
    {
        if (!TryGetPropertyCaseInsensitive(root, propertyNames, out var element))
        {
            errors.Add($"Field '{fieldLabel}' is required.");
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add($"Field '{fieldLabel}' must be a string.");
            return null;
        }

        var normalized = SourceTextNormalizer.NormalizeTitle(element.GetString());
        if (normalized.Length < minLength)
        {
            errors.Add($"Field '{fieldLabel}' must be at least {minLength} characters.");
            return null;
        }

        if (normalized.Length > maxLength)
        {
            errors.Add($"Field '{fieldLabel}' must not exceed {maxLength} characters.");
            return null;
        }

        if (ContainsUnsafeContent(normalized))
        {
            errors.Add($"Field '{fieldLabel}' contains disallowed markup or instruction text.");
            return null;
        }

        return normalized;
    }

    private static string? ReadOptionalText(
        JsonElement root,
        string[] propertyNames,
        int minLength,
        int maxLength,
        string fieldLabel,
        List<string> errors)
    {
        if (!TryGetPropertyCaseInsensitive(root, propertyNames, out var element) ||
            element.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (element.ValueKind != JsonValueKind.String)
        {
            errors.Add($"Field '{fieldLabel}' must be a string when provided.");
            return null;
        }

        var normalized = SourceTextNormalizer.NormalizeTitle(element.GetString());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        if (normalized.Length < minLength || normalized.Length > maxLength)
        {
            errors.Add($"Field '{fieldLabel}' must be between {minLength} and {maxLength} characters.");
            return null;
        }

        if (ContainsUnsafeContent(normalized))
        {
            errors.Add($"Field '{fieldLabel}' contains disallowed markup or instruction text.");
            return null;
        }

        return normalized;
    }

    private static List<ValidatedAiTopic> ReadTopics(
        JsonElement root,
        string? explicitTopic,
        AiAnalysisLimits limits,
        List<string> errors)
    {
        var maxTopics = Math.Min(limits.MaxTopics, SourceAnalysis.MaxTopics);
        var maxSubtopics = Math.Min(limits.MaxSubtopicsPerTopic, SourceAnalysis.MaxSubtopicsPerTopic);
        var result = new List<ValidatedAiTopic>();
        var seenTopics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (TryGetPropertyCaseInsensitive(root, ["topics"], out var topicsElement) &&
            topicsElement.ValueKind != JsonValueKind.Null)
        {
            if (topicsElement.ValueKind != JsonValueKind.Array)
            {
                errors.Add("Field 'topics' must be an array.");
                return result;
            }

            foreach (var item in topicsElement.EnumerateArray())
            {
                if (result.Count >= maxTopics)
                {
                    break;
                }

                if (item.ValueKind == JsonValueKind.String)
                {
                    var name = SourceTextNormalizer.NormalizeTitle(item.GetString());
                    if (!ValidateShortItem(name, SourceTopic.MaxNameLength, "topics", errors))
                    {
                        continue;
                    }

                    if (seenTopics.Add(name))
                    {
                        result.Add(new ValidatedAiTopic(name, []));
                    }
                }
                else if (item.ValueKind == JsonValueKind.Object)
                {
                    if (!TryGetPropertyCaseInsensitive(item, ["name", "title", "topic"], out var nameEl) ||
                        nameEl.ValueKind != JsonValueKind.String)
                    {
                        errors.Add("Each object in 'topics' must include a string 'name'.");
                        continue;
                    }

                    var name = SourceTextNormalizer.NormalizeTitle(nameEl.GetString());
                    if (!ValidateShortItem(name, SourceTopic.MaxNameLength, "topics", errors))
                    {
                        continue;
                    }

                    var subtopics = new List<string>();
                    if (TryGetPropertyCaseInsensitive(item, ["subtopics"], out var subEl) &&
                        subEl.ValueKind != JsonValueKind.Null)
                    {
                        if (subEl.ValueKind != JsonValueKind.Array)
                        {
                            errors.Add("Topic 'subtopics' must be an array of strings.");
                            continue;
                        }

                        var seenSubs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var sub in subEl.EnumerateArray())
                        {
                            if (subtopics.Count >= maxSubtopics)
                            {
                                break;
                            }

                            if (sub.ValueKind != JsonValueKind.String)
                            {
                                errors.Add("Every subtopic must be a string.");
                                continue;
                            }

                            var subName = SourceTextNormalizer.NormalizeTitle(sub.GetString());
                            if (!ValidateShortItem(subName, SourceTopic.MaxNameLength, "subtopics", errors))
                            {
                                continue;
                            }

                            if (!string.Equals(subName, name, StringComparison.OrdinalIgnoreCase) &&
                                seenSubs.Add(subName))
                            {
                                subtopics.Add(subName);
                            }
                        }
                    }

                    if (seenTopics.Add(name))
                    {
                        result.Add(new ValidatedAiTopic(name, subtopics));
                    }
                }
                else
                {
                    errors.Add("Items in 'topics' must be strings or topic objects.");
                }
            }
        }

        if (result.Count == 0 && !string.IsNullOrWhiteSpace(explicitTopic))
        {
            result.Add(new ValidatedAiTopic(explicitTopic, []));
        }

        if (result.Count == 0)
        {
            errors.Add("At least one valid topic is required in 'topic' or 'topics'.");
        }

        return result;
    }

    private static List<string> ReadStringArray(
        JsonElement root,
        string[] propertyNames,
        int maxCount,
        int maxItemLength,
        string fieldLabel,
        List<string> errors)
    {
        var items = new List<string>();

        if (!TryGetPropertyCaseInsensitive(root, propertyNames, out var element))
        {
            errors.Add($"Field '{fieldLabel}' is required and must be a non-empty array of strings.");
            return items;
        }

        if (element.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"Field '{fieldLabel}' must be an array of strings.");
            return items;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in element.EnumerateArray())
        {
            if (items.Count >= maxCount)
            {
                break;
            }

            if (entry.ValueKind != JsonValueKind.String)
            {
                errors.Add($"All entries in '{fieldLabel}' must be strings.");
                continue;
            }

            var value = SourceTextNormalizer.NormalizeTitle(entry.GetString());
            if (!ValidateShortItem(value, maxItemLength, fieldLabel, errors))
            {
                continue;
            }

            if (seen.Add(value))
            {
                items.Add(value);
            }
        }

        if (items.Count == 0)
        {
            errors.Add($"Field '{fieldLabel}' must contain at least one valid string.");
        }

        return items;
    }

    private static bool ValidateShortItem(
        string value,
        int maxLength,
        string fieldLabel,
        List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < MinShortFieldLength)
        {
            return false;
        }

        if (value.Length > maxLength)
        {
            errors.Add($"An item in '{fieldLabel}' exceeded {maxLength} characters.");
            return false;
        }

        if (ContainsUnsafeContent(value))
        {
            errors.Add($"An item in '{fieldLabel}' contained disallowed markup or instruction text.");
            return false;
        }

        return true;
    }

    private static bool ContainsUnsafeContent(string value)
    {
        if (HtmlTagRegex().IsMatch(value))
        {
            return true;
        }

        foreach (var marker in ForbiddenInjectionMarkers)
        {
            if (value.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetPropertyCaseInsensitive(
        JsonElement obj,
        string[] candidateNames,
        out JsonElement value)
    {
        foreach (var candidate in candidateNames)
        {
            if (obj.TryGetProperty(candidate, out value))
            {
                return true;
            }
        }

        foreach (var prop in obj.EnumerateObject())
        {
            foreach (var candidate in candidateNames)
            {
                if (string.Equals(prop.Name, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static AiOutputValidationResult Fail(string message) =>
        new(false, null, [message]);

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
