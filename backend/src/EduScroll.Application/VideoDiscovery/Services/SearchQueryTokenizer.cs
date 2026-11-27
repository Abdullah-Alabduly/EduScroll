using EduScroll.Application.VideoDiscovery.Contracts;

namespace EduScroll.Application.VideoDiscovery.Services;

/// <summary>
/// Extracts meaningful, stop-word-filtered domain tokens from discovery search queries
/// and keywords to prevent boilerplate description matches in local catalog lookups.
/// </summary>
internal static class SearchQueryTokenizer
{
    private const int MinTokenLength = 3;

    private static readonly char[] TokenSeparators =
    [
        ' ', '\t', '\r', '\n', ',', '.', ';', ':', '!', '?', '؟', '،',
        '(', ')', '[', ']', '{', '}', '"', '\'', '“', '”', '‘', '’',
        '/', '\\', '|', '=', '<', '>', '*', '&', '%', '$', '@', '^', '~', '`'
    ];

    private static readonly HashSet<string> StopWordsAndNoise = new(StringComparer.OrdinalIgnoreCase)
    {
        // English stop words
        "a", "an", "the", "and", "or", "of", "in", "on", "to", "for", "with", "by", "from",
        "using", "how", "what", "why", "is", "are", "was", "were", "that", "this", "your",
        "who", "about", "into",
        // Generic English educational/video noise
        "video", "videos", "explained", "explanation", "explain", "tutorial", "lesson",
        "lessons", "course", "lecture", "introduction", "intro", "guide", "channel",
        "youtube", "visual", "basics", "fundamentals", "overview", "walkthrough",
        // Arabic stop words & generic video noise
        "في", "من", "على", "عن", "إلى", "مع", "و", "أو", "ما", "كيف", "هو", "هي",
        "شرح", "فيديو", "درس", "كورس", "تعلم", "مقدمة"
    };

    private static readonly HashSet<string> AllowedShortDomainTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "ai", "ml", "db", "os", "3d", "2d", "c#", "go", "js", "ts", "ui", "ux", "vr", "ar"
    };

    public static IReadOnlyList<string> ExtractMeaningfulTokens(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        var parts = text.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);
        var tokens = new List<string>(parts.Length);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPart in parts)
        {
            var trimmed = rawPart.Trim('-', '_', '.', '+');
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (StopWordsAndNoise.Contains(trimmed))
            {
                continue;
            }

            if (trimmed.Length < MinTokenLength && !AllowedShortDomainTokens.Contains(trimmed))
            {
                continue;
            }

            if (seen.Add(trimmed))
            {
                tokens.Add(trimmed);
            }
        }

        return tokens;
    }

    public static string[] BuildSanitizedTerms(
        IReadOnlyList<VideoDiscoveryQueryInput> queries,
        IReadOnlyList<string>? additionalKeywords,
        int maxTerms)
    {
        if (maxTerms <= 0)
        {
            return [];
        }

        var terms = new List<string>(maxTerms);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddTokens(string? sourceText)
        {
            if (terms.Count >= maxTerms)
            {
                return;
            }

            foreach (var token in ExtractMeaningfulTokens(sourceText))
            {
                if (terms.Count >= maxTerms)
                {
                    break;
                }

                if (seen.Add(token))
                {
                    terms.Add(token);
                }
            }
        }

        foreach (var query in queries)
        {
            AddTokens(query.Query);
        }

        if (additionalKeywords is not null)
        {
            foreach (var keyword in additionalKeywords)
            {
                AddTokens(keyword);
            }
        }

        return [.. terms];
    }
}
