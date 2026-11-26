using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using EduScroll.Application.Sources.Services;

namespace EduScroll.Infrastructure.WebExtraction;

public sealed record ExtractedHtmlArticle(string Title, string Text);

/// <summary>
/// Parses HTML documents with AngleSharp, strips navigation/boilerplate/scripts/styles/ads,
/// and extracts clean readable article text with preserved paragraph boundaries.
/// </summary>
public sealed class HtmlArticleExtractor
{
    private static readonly HtmlParser Parser = new();

    private const string RemoveSelectors =
        "script, style, noscript, template, iframe, svg, canvas, video, audio, object, embed, " +
        "nav, header, footer, aside, form, button, input, select, textarea, dialog, " +
        "[hidden], [aria-hidden='true'], " +
        "[role='navigation'], [role='banner'], [role='contentinfo'], [role='complementary'], [role='dialog'], [role='alertdialog']";

    private static readonly string[] NoiseTokens =
    [
        "cookie",
        "consent",
        "gdpr",
        "banner",
        "advert",
        "ad-slot",
        "ad-container",
        "adsbox",
        "sponsored",
        "newsletter",
        "subscribe",
        "social-share",
        "share-buttons",
        "breadcrumb",
        "sidebar",
        "related-posts",
        "recommended",
        "comment-section",
        "comments",
        "popup",
        "modal",
        "paywall"
    ];

    private static readonly HashSet<string> BlockTagNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "H1", "H2", "H3", "H4", "H5", "H6",
        "P", "LI", "BLOCKQUOTE", "PRE", "FIGCAPTION", "DT", "DD"
    };

    public ExtractedHtmlArticle Extract(string html, Uri sourceUri)
    {
        ArgumentNullException.ThrowIfNull(sourceUri);

        if (string.IsNullOrWhiteSpace(html))
        {
            return new ExtractedHtmlArticle(sourceUri.Host, string.Empty);
        }

        using var document = Parser.ParseDocument(html);

        var title = ExtractTitle(document, sourceUri);

        // Remove scripts, styles, nav, headers, footers, forms, and ARIA boilerplate.
        foreach (var element in document.QuerySelectorAll(RemoveSelectors).ToArray())
        {
            element.Remove();
        }

        // Remove common ad/cookie/sidebar/comment containers while never stripping html/body/main/article.
        foreach (var element in document.Body?.QuerySelectorAll("*").ToArray() ?? [])
        {
            if (IsProtectedContainer(element))
            {
                continue;
            }

            if (LooksLikeNoiseContainer(element))
            {
                element.Remove();
            }
        }

        var contentRoot = SelectPrimaryContentRoot(document);
        var rawText = contentRoot is null
            ? string.Empty
            : ExtractReadableBlocks(contentRoot);

        var normalizedText = SourceTextNormalizer.Normalize(rawText);
        return new ExtractedHtmlArticle(title, normalizedText);
    }

    private static string ExtractTitle(IDocument document, Uri sourceUri)
    {
        var candidates = new[]
        {
            document.QuerySelector("meta[property='og:title']")?.GetAttribute("content"),
            document.QuerySelector("meta[name='twitter:title']")?.GetAttribute("content"),
            document.QuerySelector("article h1")?.TextContent,
            document.QuerySelector("main h1")?.TextContent,
            document.QuerySelector("h1")?.TextContent,
            document.Title
        };

        foreach (var candidate in candidates)
        {
            var normalized = SourceTextNormalizer.NormalizeTitle(candidate);
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                return normalized;
            }
        }

        return sourceUri.Host;
    }

    private static IElement? SelectPrimaryContentRoot(IDocument document)
    {
        var articles = document.QuerySelectorAll("article")
            .Where(a => !string.IsNullOrWhiteSpace(a.TextContent))
            .OrderByDescending(a => a.TextContent.Length)
            .ToArray();

        if (articles.Length > 0 && articles[0].TextContent.Trim().Length >= 80)
        {
            return articles[0];
        }

        var main = document.QuerySelector("main, [role='main']");
        if (main is not null && main.TextContent.Trim().Length >= 80)
        {
            return main;
        }

        return document.Body;
    }

    private static string ExtractReadableBlocks(IElement root)
    {
        var blocks = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in root.QuerySelectorAll("*"))
        {
            if (!BlockTagNames.Contains(element.TagName))
            {
                continue;
            }

            // Avoid duplicating nested block text (e.g., <p> inside <blockquote> or <li>).
            if (HasNestedBlockDescendant(element))
            {
                continue;
            }

            var blockText = SourceTextNormalizer.Normalize(element.TextContent);
            if (string.IsNullOrWhiteSpace(blockText))
            {
                continue;
            }

            if (seen.Add(blockText))
            {
                blocks.Add(blockText);
            }
        }

        if (blocks.Count > 0)
        {
            return string.Join("\n\n", blocks);
        }

        // Fallback for minimalist HTML pages that wrap text in plain <div> elements.
        return ExtractFallbackText(root);
    }

    private static bool HasNestedBlockDescendant(IElement element)
    {
        foreach (var descendant in element.QuerySelectorAll("*"))
        {
            if (BlockTagNames.Contains(descendant.TagName))
            {
                return true;
            }
        }

        return false;
    }

    private static string ExtractFallbackText(IElement root)
    {
        var sb = new StringBuilder();
        foreach (var node in root.Descendants<IText>())
        {
            var chunk = node.Data;
            if (string.IsNullOrWhiteSpace(chunk))
            {
                continue;
            }

            if (sb.Length > 0)
            {
                sb.Append('\n');
            }

            sb.Append(chunk.Trim());
        }

        return sb.ToString();
    }

    private static bool IsProtectedContainer(IElement element)
    {
        return element.TagName.Equals("HTML", StringComparison.OrdinalIgnoreCase) ||
               element.TagName.Equals("BODY", StringComparison.OrdinalIgnoreCase) ||
               element.TagName.Equals("MAIN", StringComparison.OrdinalIgnoreCase) ||
               element.TagName.Equals("ARTICLE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeNoiseContainer(IElement element)
    {
        var marker = $"{element.Id} {element.ClassName}";
        if (string.IsNullOrWhiteSpace(marker))
        {
            return false;
        }

        foreach (var token in NoiseTokens)
        {
            if (marker.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                // Avoid dropping a wrapper div that actually holds the <article> or <main>.
                if (element.QuerySelector("article, main, [role='main']") is not null)
                {
                    return false;
                }

                return true;
            }
        }

        return false;
    }
}
