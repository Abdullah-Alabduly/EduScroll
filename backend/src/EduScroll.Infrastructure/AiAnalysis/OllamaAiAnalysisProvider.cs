using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EduScroll.Application.AiAnalysis.Models;
using EduScroll.Application.AiAnalysis.Ports;
using EduScroll.Application.Exceptions;
using EduScroll.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EduScroll.Infrastructure.AiAnalysis;

/// <summary>
/// Ollama HTTP adapter implementing <see cref="IAiAnalysisProvider"/> per ADR-003 and ai-pipeline.md.
/// Enforces per-attempt timeouts, bounded exponential backoff retries on transient failures,
/// cancellation propagation, and redacted logging (never logging API keys or source text).
/// </summary>
public sealed class OllamaAiAnalysisProvider(
    IAiHttpTransport httpTransport,
    IOptions<AiProviderOptions> options,
    ILogger<OllamaAiAnalysisProvider> logger) : IAiAnalysisProvider
{
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
        var endpoint = BuildChatEndpoint(config.BaseUrl);
        var systemPrompt = AiAnalysisPromptBuilder.BuildSystemPrompt(request);
        var userPrompt = AiAnalysisPromptBuilder.BuildUserPrompt(request);

        var payload = new OllamaChatRequest(
            Model: config.Model.Trim(),
            Stream: false,
            Format: "json",
            Messages:
            [
                new OllamaChatMessage("system", systemPrompt),
                new OllamaChatMessage("user", userPrompt)
            ],
            Options: new OllamaModelOptions(config.Temperature));

        var payloadJson = JsonSerializer.Serialize(payload, SerializerOptions);
        var maxAttempts = 1 + config.MaxRetries;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sw = Stopwatch.StartNew();
            using var attemptTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptTimeoutCts.CancelAfter(TimeSpan.FromSeconds(config.TimeoutSeconds));

            try
            {
                using var httpRequest = BuildHttpRequest(endpoint, payloadJson, config.ApiKey);
                using var response = await httpTransport.SendAsync(httpRequest, attemptTimeoutCts.Token);

                sw.Stop();

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync(attemptTimeoutCts.Token);
                    var rawContent = ExtractMessageContent(responseBody);

                    logger.LogInformation(
                        "AI analysis provider request succeeded. Provider={Provider} Model={Model} PromptVersion={PromptVersion} Attempt={Attempt} DurationMs={DurationMs}",
                        config.Provider,
                        config.Model,
                        config.PromptVersion,
                        attempt,
                        sw.ElapsedMilliseconds);

                    return new AiAnalysisProviderResponse(
                        RawJson: rawContent,
                        ProviderName: config.Provider.Trim(),
                        ModelName: config.Model.Trim(),
                        PromptVersion: config.PromptVersion.Trim());
                }

                var statusCode = response.StatusCode;
                logger.LogWarning(
                    "AI analysis provider returned non-success status. Provider={Provider} Model={Model} StatusCode={StatusCode} Attempt={Attempt}/{MaxAttempts} DurationMs={DurationMs}",
                    config.Provider,
                    config.Model,
                    (int)statusCode,
                    attempt,
                    maxAttempts,
                    sw.ElapsedMilliseconds);

                if (IsTransientStatus(statusCode) && attempt < maxAttempts)
                {
                    await DelayBackoffAsync(config.RetryBaseDelayMilliseconds, attempt, cancellationToken);
                    continue;
                }

                throw MapHttpStatusError(statusCode);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Propagate caller cancellation immediately without retry.
                throw;
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                logger.LogWarning(
                    "AI analysis provider request timed out. Provider={Provider} Model={Model} Attempt={Attempt}/{MaxAttempts} TimeoutSeconds={TimeoutSeconds}",
                    config.Provider,
                    config.Model,
                    attempt,
                    maxAttempts,
                    config.TimeoutSeconds);

                if (attempt < maxAttempts)
                {
                    await DelayBackoffAsync(config.RetryBaseDelayMilliseconds, attempt, cancellationToken);
                    continue;
                }

                throw new GatewayTimeoutAppException(
                    "The AI analysis provider timed out. Please try again later.",
                    "AI_PROVIDER_TIMEOUT");
            }
            catch (HttpRequestException ex)
            {
                sw.Stop();
                logger.LogWarning(
                    ex,
                    "AI analysis provider transport error. Provider={Provider} Model={Model} Attempt={Attempt}/{MaxAttempts}",
                    config.Provider,
                    config.Model,
                    attempt,
                    maxAttempts);

                if (attempt < maxAttempts)
                {
                    await DelayBackoffAsync(config.RetryBaseDelayMilliseconds, attempt, cancellationToken);
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

    private static Uri BuildChatEndpoint(string baseUrl)
    {
        var normalized = baseUrl.Trim().TrimEnd('/') + "/";
        return new Uri(new Uri(normalized, UriKind.Absolute), "api/chat");
    }

    private static HttpRequestMessage BuildHttpRequest(Uri endpoint, string payloadJson, string? apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
        };

        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }

        return request;
    }

    private static string ExtractMessageContent(string responseBody)
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
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new UnprocessableEntityAppException(
                    "The AI provider returned an unexpected response envelope.",
                    "AI_SCHEMA_INVALID");
            }

            if (doc.RootElement.TryGetProperty("message", out var messageEl) &&
                messageEl.ValueKind == JsonValueKind.Object &&
                messageEl.TryGetProperty("content", out var contentEl) &&
                contentEl.ValueKind == JsonValueKind.String)
            {
                return contentEl.GetString() ?? string.Empty;
            }

            // Fallback support for Ollama /api/generate style envelopes if behind a compatible proxy
            if (doc.RootElement.TryGetProperty("response", out var responseEl) &&
                responseEl.ValueKind == JsonValueKind.String)
            {
                return responseEl.GetString() ?? string.Empty;
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

        // Bounded exponential backoff: base * 2^(attempt-1), capped at 10s
        var factor = 1 << Math.Min(attempt - 1, 4);
        var delayMs = Math.Min(baseDelayMs * factor, 10_000);
        return Task.Delay(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
    }

    private sealed record OllamaChatRequest(
        string Model,
        bool Stream,
        string Format,
        OllamaChatMessage[] Messages,
        OllamaModelOptions Options);

    private sealed record OllamaChatMessage(string Role, string Content);

    private sealed record OllamaModelOptions(double Temperature);
}
