using System.Text;

namespace EduScroll.Application.Sources.Services;

/// <summary>
/// Normalizes extracted UTF-8 educational text while preserving meaningful paragraph boundaries.
/// </summary>
public static class SourceTextNormalizer
{
    public static string Normalize(string? rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return string.Empty;
        }

        // Normalize CRLF/CR/Unicode line separators and strip unsafe control chars.
        var cleaned = new StringBuilder(rawText.Length);
        for (var i = 0; i < rawText.Length; i++)
        {
            var c = rawText[i];
            if (c == '\r')
            {
                if (i + 1 < rawText.Length && rawText[i + 1] == '\n')
                {
                    i++;
                }

                cleaned.Append('\n');
                continue;
            }

            if (c is '\u2028' or '\u2029')
            {
                cleaned.Append('\n');
                continue;
            }

            if (c == '\t')
            {
                cleaned.Append(' ');
                continue;
            }

            if (c == '\n')
            {
                cleaned.Append('\n');
                continue;
            }

            if (c == '\u00A0')
            {
                cleaned.Append(' ');
                continue;
            }

            // Skip NUL and other C0/C1 control characters
            if (char.IsControl(c))
            {
                continue;
            }

            cleaned.Append(c);
        }

        var lines = cleaned.ToString().Split('\n');
        var paragraphs = new List<string>();
        var currentParagraph = new StringBuilder();

        foreach (var rawLine in lines)
        {
            var collapsed = CollapseSpaces(rawLine);
            if (collapsed.Length == 0)
            {
                if (currentParagraph.Length > 0)
                {
                    paragraphs.Add(currentParagraph.ToString());
                    currentParagraph.Clear();
                }

                continue;
            }

            if (currentParagraph.Length > 0)
            {
                currentParagraph.Append('\n');
            }

            currentParagraph.Append(collapsed);
        }

        if (currentParagraph.Length > 0)
        {
            paragraphs.Add(currentParagraph.ToString());
        }

        return string.Join("\n\n", paragraphs);
    }

    public static string NormalizeTitle(string? rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return string.Empty;
        }

        var cleaned = new StringBuilder(rawTitle.Length);
        foreach (var c in rawTitle)
        {
            if (char.IsControl(c) || c == '\r' || c == '\n' || c == '\t' || c == '\u00A0')
            {
                cleaned.Append(' ');
            }
            else
            {
                cleaned.Append(c);
            }
        }

        return CollapseSpaces(cleaned.ToString());
    }

    public static string BuildPreview(string normalizedText, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(normalizedText) || maxLength <= 0)
        {
            return string.Empty;
        }

        var singleLine = CollapseSpaces(normalizedText.Replace('\n', ' '));
        if (singleLine.Length <= maxLength)
        {
            return singleLine;
        }

        if (maxLength <= 3)
        {
            return singleLine[..maxLength];
        }

        var cut = singleLine[..(maxLength - 1)].TrimEnd();
        var lastSpace = cut.LastIndexOf(' ');
        if (lastSpace > maxLength / 2)
        {
            cut = cut[..lastSpace].TrimEnd();
        }

        return cut + "…";
    }

    private static string CollapseSpaces(string input)
    {
        var sb = new StringBuilder(input.Length);
        var previousWasSpace = true;

        foreach (var c in input)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!previousWasSpace)
                {
                    sb.Append(' ');
                    previousWasSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                previousWasSpace = false;
            }
        }

        if (sb.Length > 0 && sb[^1] == ' ')
        {
            sb.Length--;
        }

        return sb.ToString();
    }
}
