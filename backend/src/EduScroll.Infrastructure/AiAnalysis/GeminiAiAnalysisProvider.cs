using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EduScroll.Application.AiAnalysis.Models;
using EduScroll.Application.AiAnalysis.Ports;
using EduScroll.Application.Exceptions;
using EduScroll.Domain.Entities;
using EduScroll.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduScroll.Infrastructure.AiAnalysis;

/// <summary>
/// Google Gemini Developer API adapter implementing <see cref="IAiAnalysisProvider"/>.
/// Uses <c>POST {BaseUrl}/models/{Model}:generateContent</c> with <c>x-goog-api-key</c> header auth,
/// Gemini Structured Outputs (<c>responseMimeType = "application/json"</c> + <c>responseJsonSchema</c>),
/// bounded transient retries, caller cancellation propagation, and strict API key redaction.
/// </summary>
public sealed class GeminiAiAnalysisProvider(
    HttpClient httpClient,
    IOptions<AiProviderOptions> options,
    ILogger<GeminiAiAnalysisProvider> logger) : IAiAnalysisProvider
{
    private const string ProviderDisplayName = "Gemini";
    private const string ApiKeyHeaderName = "x-goog-api-key";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public async Task<AiAnalysisProviderResponse> AnalyzeAsync(
        AiAnalysisProviderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var config = options.Value;
        var gemini = config.Gemini ?? new GeminiProviderOptions();

        if (string.IsNullOrWhiteSpace(gemini.ApiKey))
        {
            throw new ExternalDependencyAppException(
                "AI provider authentication failed. Verify server AI credentials.",
                "AI_PROVIDER_AUTH_FAILED");
        }

        var apiKey = gemini.ApiKey.Trim();
        var modelName = NormalizeModelName(gemini.Model);
        var endpoint = BuildGenerateContentEndpoint(gemini.BaseUrl, modelName);

        var payloadJson = BuildRequestPayloadJson(request, config);
        var maxAttempts = 1 + Math.Max(0, gemini.MaxRetries);
        var timeoutSeconds = Math.Clamp(gemini.TimeoutSeconds, 1, 300);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stopwatch = Stopwatch.StartNew();
            using var attemptTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));

            try
            {
                using var httpRequest = BuildHttpRequest(endpoint, payloadJson, apiKey);
                using var response = await httpClient.SendAsync(
                    httpRequest,
                    HttpCompletionOption.ResponseContentRead,
                    attemptTimeoutCts.Token);

                stopwatch.Stop();

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync(attemptTimeoutCts.Token);
                    var rawContent = ExtractResponseJsonContent(responseBody);

                    logger.LogInformation(
                        "AI analysis provider request succeeded. Provider={Provider} Model={Model} PromptVersion={PromptVersion} Attempt={Attempt} DurationMs={DurationMs}",
                        ProviderDisplayName,
                        modelName,
                        config.PromptVersion,
                        attempt,
                        stopwatch.ElapsedMilliseconds);

                    return new AiAnalysisProviderResponse(
                        RawJson: rawContent,
                        ProviderName: ProviderDisplayName,
                        ModelName: modelName,
                        PromptVersion: config.PromptVersion.Trim());
                }

                var statusCode = response.StatusCode;
                logger.LogWarning(
                    "AI analysis provider returned non-success status. Provider={Provider} Model={Model} StatusCode={StatusCode} Attempt={Attempt}/{MaxAttempts} DurationMs={DurationMs}",
                    ProviderDisplayName,
                    modelName,
                    (int)statusCode,
                    attempt,
                    maxAttempts,
                    stopwatch.ElapsedMilliseconds);

                if (IsTransientStatus(statusCode) && attempt < maxAttempts)
                {
                    await DelayBackoffAsync(gemini.RetryBaseDelayMilliseconds, attempt, cancellationToken);
                    continue;
                }

                throw MapHttpStatusError(statusCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                stopwatch.Stop();
                logger.LogWarning(
                    "AI analysis provider request timed out. Provider={Provider} Model={Model} Attempt={Attempt}/{MaxAttempts} TimeoutSeconds={TimeoutSeconds}",
                    ProviderDisplayName,
                    modelName,
                    attempt,
                    maxAttempts,
                    timeoutSeconds);

                if (attempt < maxAttempts)
                {
                    await DelayBackoffAsync(gemini.RetryBaseDelayMilliseconds, attempt, cancellationToken);
                    continue;
                }

                throw new GatewayTimeoutAppException(
                    "The AI analysis provider timed out. Please try again later.",
                    "AI_PROVIDER_TIMEOUT");
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                var sanitizedMessage = RedactSecret(ex.Message, apiKey);
                logger.LogWarning(
                    "AI analysis provider transport error. Provider={Provider} Model={Model} Attempt={Attempt}/{MaxAttempts} Error={Error}",
                    ProviderDisplayName,
                    modelName,
                    attempt,
                    maxAttempts,
                    sanitizedMessage);

                if (attempt < maxAttempts)
                {
                    await DelayBackoffAsync(gemini.RetryBaseDelayMilliseconds, attempt, cancellationToken);
                    continue;
                }

                throw new ExternalDependencyAppException(
                    "The AI analysis provider is currently unreachable. Please try again later.",
                    "AI_PROVIDER_UNAVAILABLE");
            }
        }

        throw new ExternalDependencyAppException(
            "The AI analysis provider is currently unavailable.",
            "AI_PROVIDER_UNAVAILABLE");
    }

    private static string NormalizeModelName(string? rawModel)
    {
        var trimmed = rawModel?.Trim() ?? string.Empty;
        const string modelsPrefix = "models/";
        return trimmed.StartsWith(modelsPrefix, StringComparison.OrdinalIgnoreCase)
            ? trimmed[modelsPrefix.Length..]
            : trimmed;
    }

    private static Uri BuildGenerateContentEndpoint(string? baseUrl, string modelName)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(modelName))
        {
            throw new ExternalDependencyAppException(
                "AI provider configuration is invalid.",
                "AI_PROVIDER_UNAVAILABLE");
        }

        var normalizedBase = baseUrl.Trim().TrimEnd('/');
        var escapedModel = Uri.EscapeDataString(modelName);
        var endpointString = $"{normalizedBase}/models/{escapedModel}:generateContent";

        if (!Uri.TryCreate(endpointString, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(uri.Host))
        {
            throw new ExternalDependencyAppException(
                "AI provider configuration is invalid.",
                "AI_PROVIDER_UNAVAILABLE");
        }

        return uri;
    }

    private static HttpRequestMessage BuildHttpRequest(Uri endpoint, string payloadJson, string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation(ApiKeyHeaderName, apiKey);

        return request;
    }

    private static string BuildRequestPayloadJson(AiAnalysisProviderRequest request, AiProviderOptions config)
    {
        var systemPrompt = AiAnalysisPromptBuilder.BuildSystemPrompt(request);
        var userPrompt = AiAnalysisPromptBuilder.BuildUserPrompt(request);
        var schema = BuildOutputJsonSchema(request, config);

        var payload = new
        {
            systemInstruction = new
            {
                parts = new[]
                {
                    new { text = systemPrompt }
                }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[]
                    {
                        new { text = userPrompt }
                    }
                }
            },
            generationConfig = new
            {
                temperature = config.Temperature,
                responseMimeType = "application/json",
                responseJsonSchema = schema
            }
        };

        return JsonSerializer.Serialize(payload, SerializerOptions);
    }

    private static object BuildOutputJsonSchema(AiAnalysisProviderRequest request, AiProviderOptions config)
    {
        var allowedLanguages = request.TargetFeedLanguage switch
        {
            TargetFeedLanguage.En => new[] { SourceSearchQuery.EnglishCode },
            TargetFeedLanguage.Ar => new[] { SourceSearchQuery.ArabicCode },
            _ => new[] { SourceSearchQuery.EnglishCode, SourceSearchQuery.ArabicCode }
        };

        var (minQueries, maxQueries) = request.TargetFeedLanguage == TargetFeedLanguage.Both
            ? (request.DualLanguageMinSearchQueriesPerLanguage * 2, request.DualLanguageMaxSearchQueriesPerLanguage * 2)
            : (request.SingleLanguageMinSearchQueries, request.SingleLanguageMaxSearchQueries);

        var maxSummaryLength = Math.Clamp(config.MaxSummaryLength, 50, SourceAnalysis.MaxSummaryLength);

        return new Dictionary<string, object?>
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["required"] = new[]
            {
                "subject",
                "topic",
                "summary",
                "topics",
                "concepts",
                "keywords",
                "searchQueries"
            },
            ["properties"] = new Dictionary<string, object?>
            {
                ["subject"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["minLength"] = 2,
                    ["maxLength"] = SourceAnalysis.MaxSubjectLength
                },
                ["topic"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["minLength"] = 2,
                    ["maxLength"] = SourceTopic.MaxNameLength
                },
                ["summary"] = new Dictionary<string, object?>
                {
                    ["type"] = "string",
                    ["minLength"] = 20,
                    ["maxLength"] = maxSummaryLength
                },
                ["topics"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["maxItems"] = request.MaxTopics,
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new[] { "name", "subtopics" },
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["name"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["subtopics"] = new Dictionary<string, object?>
                            {
                                ["type"] = "array",
                                ["maxItems"] = request.MaxSubtopicsPerTopic,
                                ["items"] = new Dictionary<string, object?> { ["type"] = "string" }
                            }
                        }
                    }
                },
                ["concepts"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["maxItems"] = request.MaxConcepts,
                    ["items"] = new Dictionary<string, object?> { ["type"] = "string" }
                },
                ["keywords"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["minItems"] = 1,
                    ["maxItems"] = request.MaxKeywords,
                    ["items"] = new Dictionary<string, object?> { ["type"] = "string" }
                },
                ["searchQueries"] = new Dictionary<string, object?>
                {
                    ["type"] = "array",
                    ["minItems"] = Math.Max(1, minQueries),
                    ["maxItems"] = Math.Max(1, maxQueries),
                    ["items"] = new Dictionary<string, object?>
                    {
                        ["type"] = "object",
                        ["additionalProperties"] = false,
                        ["required"] = new[] { "query", "language" },
                        ["properties"] = new Dictionary<string, object?>
                        {
                            ["query"] = new Dictionary<string, object?> { ["type"] = "string" },
                            ["language"] = new Dictionary<string, object?>
                            {
                                ["type"] = "string",
                                ["enum"] = allowedLanguages
                            }
                        }
                    }
                }
            }
        };
    }

    private static string ExtractResponseJsonContent(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            throw new UnprocessableEntityAppException(
                "The AI provider returned an empty response.",
                "AI_SCHEMA_INVALID");
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("candidates", out var candidatesEl) ||
                candidatesEl.ValueKind != JsonValueKind.Array)
            {
                throw new UnprocessableEntityAppException(
                    "The AI provider returned an unexpected response envelope.",
                    "AI_SCHEMA_INVALID");
            }

            foreach (var candidate in candidatesEl.EnumerateArray())
            {
                if (candidate.ValueKind != JsonValueKind.Object ||
                    !candidate.TryGetProperty("content", out var contentEl) ||
                    contentEl.ValueKind != JsonValueKind.Object ||
                    !contentEl.TryGetProperty("parts", out var partsEl) ||
                    partsEl.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var sb = new StringBuilder();
                foreach (var part in partsEl.EnumerateArray())
                {
                    if (part.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (part.TryGetProperty("thought", out var thoughtEl) &&
                        thoughtEl.ValueKind == JsonValueKind.True)
                    {
                        continue;
                    }

                    if (part.TryGetProperty("text", out var textEl) &&
                        textEl.ValueKind == JsonValueKind.String)
                    {
                        sb.Append(textEl.GetString());
                    }
                }

                if (sb.Length > 0)
                {
                    return sb.ToString();
                }
            }

            throw new UnprocessableEntityAppException(
                "The AI provider response did not include analysis content.",
                "AI_SCHEMA_INVALID");
        }
        catch (JsonException)
        {
            throw new UnprocessableEntityAppException(
                "The AI provider returned a malformed response envelope.",
                "AI_SCHEMA_INVALID");
        }
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.RequestTimeout
            or HttpStatusCode.TooManyRequests
            or HttpStatusCode.InternalServerError
            or HttpStatusCode.BadGateway
            or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout;

    private static AppException MapHttpStatusError(HttpStatusCode statusCode) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                new ExternalDependencyAppException(
                    "AI provider authentication failed. Verify server AI credentials.",
                    "AI_PROVIDER_AUTH_FAILED"),
            HttpStatusCode.TooManyRequests =>
                new ExternalDependencyAppException(
                    "The AI provider is temporarily rate-limited. Please try again shortly.",
                    "AI_PROVIDER_RATE_LIMITED"),
            HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout =>
                new GatewayTimeoutAppException(
                    "The AI analysis provider timed out. Please try again later.",
                    "AI_PROVIDER_TIMEOUT"),
            _ => new ExternalDependencyAppException(
                "The AI analysis provider is currently unavailable. Please try again later.",
                "AI_PROVIDER_UNAVAILABLE")
        };

    private static Task DelayBackoffAsync(int baseDelayMs, int attempt, CancellationToken cancellationToken)
    {
        if (baseDelayMs <= 0)
        {
            return Task.CompletedTask;
        }

        var factor = 1 << Math.Min(attempt - 1, 4);
        var delayMs = (int)Math.Min((long)baseDelayMs * factor, 10_000L);
        return Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
    }

    private static string RedactSecret(string? text, string apiKey)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sanitized = text.Replace(apiKey, "[REDACTED]", StringComparison.Ordinal);
        var escaped = Uri.EscapeDataString(apiKey);
        if (!string.IsNullOrEmpty(escaped))
        {
            sanitized = sanitized.Replace(escaped, "[REDACTED]", StringComparison.OrdinalIgnoreCase);
        }

        return sanitized;
    }
}
