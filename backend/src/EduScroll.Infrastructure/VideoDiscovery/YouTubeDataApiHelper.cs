using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using EduScroll.Application.Exceptions;

namespace EduScroll.Infrastructure.VideoDiscovery;

internal static partial class YouTubeDataApiHelper
{
    private static readonly string[] ThumbnailPriorityKeys =
    [
        "high",
        "medium",
        "standard",
        "maxres",
        "default"
    ];

    internal enum ForbiddenErrorKind
    {
        AuthFailed,
        QuotaExceeded,
        RateLimited
    }

    public static HttpRequestMessage BuildHttpRequest(Uri endpoint)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    public static Uri BuildEndpointUri(string? apiBaseUrl, string relativePathAndQuery)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            throw new ExternalDependencyAppException(
                "YouTube Data API base URL is not configured.",
                "YOUTUBE_API_CONFIGURATION_INVALID");
        }

        var normalizedBase = apiBaseUrl.Trim().TrimEnd('/');
        var normalizedRelative = relativePathAndQuery.TrimStart('/');
        var endpointString = $"{normalizedBase}/{normalizedRelative}";

        if (!Uri.TryCreate(endpointString, UriKind.Absolute, out var endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(endpoint.Host))
        {
            throw new ExternalDependencyAppException(
                "YouTube Data API base URL is invalid.",
                "YOUTUBE_API_CONFIGURATION_INVALID");
        }

        return endpoint;
    }

    public static ForbiddenErrorKind ClassifyForbiddenError(string? responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return ForbiddenErrorKind.AuthFailed;
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !TryGetObject(doc.RootElement, "error", out var errorEl))
            {
                return ForbiddenErrorKind.AuthFailed;
            }

            var hasRateLimitReason = false;

            if (errorEl.TryGetProperty("errors", out var errorsEl) &&
                errorsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in errorsEl.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var reason = GetOptionalTrimmedString(entry, "reason");
                    if (IsQuotaReason(reason))
                    {
                        return ForbiddenErrorKind.QuotaExceeded;
                    }

                    if (IsRateLimitReason(reason))
                    {
                        hasRateLimitReason = true;
                    }

                    var entryMessage = GetOptionalTrimmedString(entry, "message");
                    if (ContainsQuotaKeyword(entryMessage))
                    {
                        return ForbiddenErrorKind.QuotaExceeded;
                    }
                }
            }

            var errorMessage = GetOptionalTrimmedString(errorEl, "message");
            if (ContainsQuotaKeyword(errorMessage))
            {
                return ForbiddenErrorKind.QuotaExceeded;
            }

            return hasRateLimitReason
                ? ForbiddenErrorKind.RateLimited
                : ForbiddenErrorKind.AuthFailed;
        }
        catch (JsonException)
        {
            return ForbiddenErrorKind.AuthFailed;
        }
    }

    public static AppException MapForbiddenError(ForbiddenErrorKind kind) =>
        kind switch
        {
            ForbiddenErrorKind.QuotaExceeded => new ExternalDependencyAppException(
                "YouTube Data API quota has been exceeded.",
                "YOUTUBE_API_QUOTA_EXCEEDED"),
            ForbiddenErrorKind.RateLimited => new ExternalDependencyAppException(
                "YouTube Data API is currently rate-limited. Please try again shortly.",
                "YOUTUBE_API_RATE_LIMITED"),
            _ => new ExternalDependencyAppException(
                "YouTube Data API authentication or authorization failed.",
                "YOUTUBE_API_AUTH_FAILED")
        };

    public static AppException MapHttpStatusError(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new ExternalDependencyAppException(
                    "YouTube Data API authentication or authorization failed.",
                    "YOUTUBE_API_AUTH_FAILED"),
            HttpStatusCode.TooManyRequests =>
                new ExternalDependencyAppException(
                    "YouTube Data API is currently rate-limited. Please try again shortly.",
                    "YOUTUBE_API_RATE_LIMITED"),
            HttpStatusCode.RequestTimeout =>
                new GatewayTimeoutAppException(
                    "YouTube Data API request timed out. Please try again later.",
                    "YOUTUBE_API_TIMEOUT"),
            _ => new ExternalDependencyAppException(
                "YouTube Data API is currently unavailable. Please try again later.",
                "YOUTUBE_API_UNAVAILABLE")
        };

    public static string RedactSensitiveText(string? text, string? apiKey)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sanitized = text;
        if (!string.IsNullOrEmpty(apiKey))
        {
            sanitized = sanitized.Replace(apiKey, "[REDACTED]", StringComparison.Ordinal);

            var escapedKey = Uri.EscapeDataString(apiKey);
            if (!string.IsNullOrEmpty(escapedKey))
            {
                sanitized = sanitized.Replace(escapedKey, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
            }
        }

        return KeyQueryParameterRegex().Replace(sanitized, "$1[REDACTED]");
    }

    public static string? ExtractBestThumbnailUrl(JsonElement snippet)
    {
        if (!TryGetObject(snippet, "thumbnails", out var thumbnails))
        {
            return null;
        }

        foreach (var key in ThumbnailPriorityKeys)
        {
            if (TryGetObject(thumbnails, key, out var thumbnail) &&
                GetOptionalTrimmedString(thumbnail, "url") is { } url)
            {
                return url;
            }
        }

        return null;
    }

    public static bool TryGetObject(JsonElement element, string propertyName, out JsonElement obj)
    {
        if (element.TryGetProperty(propertyName, out obj) &&
            obj.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        obj = default;
        return false;
    }

    public static string? GetOptionalTrimmedString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var prop) ||
            prop.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var raw = prop.GetString();
        return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim();
    }

    public static DateTimeOffset? TryGetDateTimeOffset(JsonElement element, string propertyName)
    {
        var raw = GetOptionalTrimmedString(element, propertyName);
        if (raw is null)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
            out var parsed)
            ? parsed
            : null;
    }

    private static bool IsQuotaReason(string? reason) =>
        string.Equals(reason, "quotaExceeded", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(reason, "dailyLimitExceeded", StringComparison.OrdinalIgnoreCase);

    private static bool IsRateLimitReason(string? reason) =>
        string.Equals(reason, "rateLimitExceeded", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(reason, "userRateLimitExceeded", StringComparison.OrdinalIgnoreCase);

    private static bool ContainsQuotaKeyword(string? message) =>
        !string.IsNullOrWhiteSpace(message) &&
        message.Contains("quota", StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"([?&]key=)[^&\s""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KeyQueryParameterRegex();
}
