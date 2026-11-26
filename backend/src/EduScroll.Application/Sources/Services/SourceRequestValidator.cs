using EduScroll.Application.Exceptions;
using EduScroll.Application.Sources.Contracts;
using EduScroll.Application.Sources.Models;
using EduScroll.Domain.Entities;

namespace EduScroll.Application.Sources.Services;

public static class SourceRequestValidator
{
    public static (string Title, string NormalizedText, string? FileName) ValidateAndNormalizePdfRequest(
        CreatePdfSourceRequest? request,
        SourceIngestionLimits limits)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (request is null)
        {
            throw new ValidationAppException(
                "Source request body is required.",
                new Dictionary<string, string[]> { ["request"] = ["Request body cannot be null."] },
                "SOURCE_INVALID_REQUEST");
        }

        var normalizedTitle = SourceTextNormalizer.NormalizeTitle(request.Title);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            AddError(errors, "title", "Title is required.");
        }
        else if (normalizedTitle.Length > Source.MaxTitleLength)
        {
            AddError(errors, "title", $"Title must not exceed {Source.MaxTitleLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(request.SourceType) ||
            !string.Equals(request.SourceType.Trim(), nameof(SourceType.Pdf), StringComparison.OrdinalIgnoreCase))
        {
            AddError(errors, "sourceType", "SourceType must be 'Pdf' for this endpoint.");
        }

        var normalizedText = SourceTextNormalizer.Normalize(request.ExtractedText);
        if (string.IsNullOrWhiteSpace(normalizedText))
        {
            AddError(errors, "extractedText", "Extracted text is required.");
        }
        else if (normalizedText.Length < limits.MinExtractedTextLength)
        {
            AddError(
                errors,
                "extractedText",
                $"Extracted text must contain at least {limits.MinExtractedTextLength} characters.");
        }
        else if (normalizedText.Length > limits.MaxExtractedTextLength)
        {
            AddError(
                errors,
                "extractedText",
                $"Extracted text must not exceed {limits.MaxExtractedTextLength:N0} characters.");
        }

        string? safeFileName = null;
        if (!string.IsNullOrWhiteSpace(request.FileName))
        {
            safeFileName = SanitizeFileName(request.FileName);
            if (safeFileName.Length > Source.MaxOriginalFileNameLength)
            {
                AddError(
                    errors,
                    "fileName",
                    $"File name must not exceed {Source.MaxOriginalFileNameLength} characters.");
            }
        }

        if (errors.Count > 0)
        {
            var code = DeterminePdfErrorCode(errors, normalizedText, limits);
            throw new ValidationAppException(
                "One or more source validation errors occurred.",
                errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray()),
                code);
        }

        return (normalizedTitle, normalizedText, safeFileName);
    }

    public static Uri ValidateAndParseUrlRequest(CreateUrlSourceRequest? request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Url))
        {
            throw new ValidationAppException(
                "Article URL is required.",
                new Dictionary<string, string[]> { ["url"] = ["A valid HTTP or HTTPS article URL is required."] },
                "URL_REQUIRED");
        }

        var trimmed = request.Url.Trim();
        if (trimmed.Length > Source.MaxOriginalUrlLength)
        {
            throw new ValidationAppException(
                "Article URL is too long.",
                new Dictionary<string, string[]> { ["url"] = [$"URL must not exceed {Source.MaxOriginalUrlLength} characters."] },
                "URL_TOO_LONG");
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
        {
            throw new ValidationAppException(
                "The supplied URL is malformed.",
                new Dictionary<string, string[]> { ["url"] = ["Enter a valid absolute URL starting with http:// or https://."] },
                "URL_INVALID");
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new ValidationAppException(
                "Only HTTP and HTTPS URLs are supported.",
                new Dictionary<string, string[]> { ["url"] = ["Only http:// and https:// URLs are allowed."] },
                "URL_INVALID_SCHEME");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ValidationAppException(
                "The supplied URL must include a valid host name.",
                new Dictionary<string, string[]> { ["url"] = ["URL must include a valid domain name."] },
                "URL_INVALID");
        }

        if (!string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new ValidationAppException(
                "URLs containing embedded credentials are not allowed.",
                new Dictionary<string, string[]> { ["url"] = ["URLs must not include embedded credentials."] },
                "URL_INVALID");
        }

        return uri;
    }

    public static (int Page, int PageSize) ValidatePagination(
        int page,
        int pageSize,
        SourceIngestionLimits limits)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        if (page < 1)
        {
            AddError(errors, "page", "Page must be greater than or equal to 1.");
        }

        var effectivePageSize = pageSize == 0 ? limits.DefaultPageSize : pageSize;
        if (effectivePageSize < 1 || effectivePageSize > limits.MaxPageSize)
        {
            AddError(errors, "pageSize", $"PageSize must be between 1 and {limits.MaxPageSize}.");
        }

        if (errors.Count > 0)
        {
            throw new ValidationAppException(
                "Pagination parameters are invalid.",
                errors.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray()),
                "INVALID_PAGINATION");
        }

        return (page, effectivePageSize);
    }

    private static string SanitizeFileName(string rawFileName)
    {
        // Strip any directory separators so client path info is never persisted.
        var normalized = rawFileName.Trim().Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        var baseName = lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
        return SourceTextNormalizer.NormalizeTitle(baseName);
    }

    private static string DeterminePdfErrorCode(
        Dictionary<string, List<string>> errors,
        string normalizedText,
        SourceIngestionLimits limits)
    {
        if (errors.ContainsKey("extractedText"))
        {
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                return "SOURCE_TEXT_REQUIRED";
            }

            if (normalizedText.Length < limits.MinExtractedTextLength)
            {
                return "SOURCE_TEXT_TOO_SHORT";
            }

            if (normalizedText.Length > limits.MaxExtractedTextLength)
            {
                return "SOURCE_TEXT_TOO_LONG";
            }
        }

        if (errors.ContainsKey("title"))
        {
            return "SOURCE_TITLE_INVALID";
        }

        if (errors.ContainsKey("sourceType"))
        {
            return "SOURCE_TYPE_INVALID";
        }

        return "SOURCE_VALIDATION_FAILED";
    }

    private static void AddError(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list))
        {
            list = [];
            errors[key] = list;
        }

        list.Add(message);
    }
}
