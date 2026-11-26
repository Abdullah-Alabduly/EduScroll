using EduScroll.Domain.Exceptions;

namespace EduScroll.Domain.Entities;

/// <summary>
/// Represents an educational content source (extracted PDF text or web article text)
/// owned exclusively by a single authenticated learner.
/// </summary>
public sealed class Source
{
    public const int MaxTitleLength = 250;
    public const int MaxOriginalUrlLength = 2048;
    public const int MaxOriginalFileNameLength = 255;
    public const int MinExtractedTextLength = 50;
    public const int DefaultMaxExtractedTextLength = 200_000;

    private Source()
    {
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public SourceType SourceType { get; private set; }

    public string? OriginalUrl { get; private set; }

    public string? OriginalFileName { get; private set; }

    public string ExtractedText { get; private set; } = string.Empty;

    public int ContentLength { get; private set; }

    public SourceStatus Status { get; private set; }

    public DateTimeOffset CreatedAtUtc { get; private set; }

    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool IsOwnedBy(Guid userId) => userId != Guid.Empty && OwnerUserId == userId;

    public static Source CreatePdf(
        Guid ownerUserId,
        string title,
        string extractedText,
        DateTimeOffset utcNow,
        string? originalFileName = null,
        int minContentLength = MinExtractedTextLength,
        int maxContentLength = DefaultMaxExtractedTextLength)
    {
        ValidateCommon(
            ownerUserId,
            title,
            extractedText,
            minContentLength,
            maxContentLength,
            out var normalizedTitle,
            out var normalizedText);

        string? normalizedFileName = null;
        if (!string.IsNullOrWhiteSpace(originalFileName))
        {
            normalizedFileName = originalFileName.Trim();
            if (normalizedFileName.Length > MaxOriginalFileNameLength)
            {
                throw new InvalidSourceDomainException(
                    "SOURCE_FILENAME_TOO_LONG",
                    $"File name must not exceed {MaxOriginalFileNameLength} characters.");
            }
        }

        return new Source
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Title = normalizedTitle,
            SourceType = SourceType.Pdf,
            OriginalUrl = null,
            OriginalFileName = normalizedFileName,
            ExtractedText = normalizedText,
            ContentLength = normalizedText.Length,
            Status = SourceStatus.Completed,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow
        };
    }

    public static Source CreateWebArticle(
        Guid ownerUserId,
        string title,
        string originalUrl,
        string extractedText,
        DateTimeOffset utcNow,
        int minContentLength = MinExtractedTextLength,
        int maxContentLength = DefaultMaxExtractedTextLength)
    {
        ValidateCommon(
            ownerUserId,
            title,
            extractedText,
            minContentLength,
            maxContentLength,
            out var normalizedTitle,
            out var normalizedText);

        if (string.IsNullOrWhiteSpace(originalUrl))
        {
            throw new InvalidSourceDomainException(
                "SOURCE_URL_REQUIRED",
                "Original URL is required for web article sources.");
        }

        var trimmedUrl = originalUrl.Trim();
        if (trimmedUrl.Length > MaxOriginalUrlLength)
        {
            throw new InvalidSourceDomainException(
                "SOURCE_URL_TOO_LONG",
                $"Original URL must not exceed {MaxOriginalUrlLength} characters.");
        }

        if (!Uri.TryCreate(trimmedUrl, UriKind.Absolute, out var parsedUri) ||
            (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(parsedUri.Host))
        {
            throw new InvalidSourceDomainException(
                "SOURCE_URL_INVALID",
                "Original URL must be a valid HTTP or HTTPS address.");
        }

        return new Source
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Title = normalizedTitle,
            SourceType = SourceType.WebArticle,
            OriginalUrl = trimmedUrl,
            OriginalFileName = null,
            ExtractedText = normalizedText,
            ContentLength = normalizedText.Length,
            Status = SourceStatus.Completed,
            CreatedAtUtc = utcNow,
            UpdatedAtUtc = utcNow
        };
    }

    private static void ValidateCommon(
        Guid ownerUserId,
        string title,
        string extractedText,
        int minContentLength,
        int maxContentLength,
        out string normalizedTitle,
        out string normalizedText)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new InvalidSourceDomainException(
                "SOURCE_INVALID_OWNER",
                "Source must be associated with a valid owner user identifier.");
        }

        if (minContentLength < 1 || maxContentLength < minContentLength)
        {
            throw new InvalidSourceDomainException(
                "SOURCE_INVALID_LIMITS",
                "Source content length limits are invalid.");
        }

        if (string.IsNullOrWhiteSpace(title))
        {
            throw new InvalidSourceDomainException(
                "SOURCE_TITLE_REQUIRED",
                "Source title is required.");
        }

        normalizedTitle = title.Trim();
        if (normalizedTitle.Length > MaxTitleLength)
        {
            throw new InvalidSourceDomainException(
                "SOURCE_TITLE_TOO_LONG",
                $"Source title must not exceed {MaxTitleLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(extractedText))
        {
            throw new InvalidSourceDomainException(
                "SOURCE_TEXT_REQUIRED",
                "Extracted text must not be empty.");
        }

        normalizedText = extractedText.Trim();
        if (normalizedText.Length < minContentLength)
        {
            throw new InvalidSourceDomainException(
                "SOURCE_TEXT_TOO_SHORT",
                $"Extracted text must be at least {minContentLength} characters.");
        }

        if (normalizedText.Length > maxContentLength)
        {
            throw new InvalidSourceDomainException(
                "SOURCE_TEXT_TOO_LONG",
                $"Extracted text must not exceed {maxContentLength} characters.");
        }
    }
}
