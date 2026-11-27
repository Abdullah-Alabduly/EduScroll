using EduScroll.Domain.Entities;

namespace EduScroll.Infrastructure.Configuration;

public sealed class AiProviderOptions
{
    public const string SectionName = "AiProvider";

    public string Provider { get; set; } = "Ollama";

    public string BaseUrl { get; set; } = "http://localhost:11434";

    public string Model { get; set; } = "llama3.2";

    /// <summary>
    /// Optional API key for authenticated Ollama proxies or hosted gateways.
    /// Never commit real keys to appsettings*.json; inject via User Secrets or environment variables.
    /// </summary>
    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 60;

    public int MaxRetries { get; set; } = 2;

    public int RetryBaseDelayMilliseconds { get; set; } = 500;

    public int MinInputCharacters { get; set; } = Source.MinExtractedTextLength;

    public int MaxInputCharacters { get; set; } = 24_000;

    public int MaxSchemaRepairAttempts { get; set; } = 1;

    public int MaxSummaryLength { get; set; } = 1_200;

    public int MaxTopics { get; set; } = 5;

    public int MaxSubtopicsPerTopic { get; set; } = 4;

    public int MaxConcepts { get; set; } = 10;

    public int MaxKeywords { get; set; } = 12;

    public int SingleLanguageMinSearchQueries { get; set; } = 3;

    public int SingleLanguageMaxSearchQueries { get; set; } = 8;

    public int DualLanguageMinSearchQueriesPerLanguage { get; set; } = 2;

    public int DualLanguageMaxSearchQueriesPerLanguage { get; set; } = 5;

    public double Temperature { get; set; } = 0.1;

    public string PromptVersion { get; set; } = "v1.1";

    public GeminiProviderOptions Gemini { get; set; } = new();
}

public sealed class GeminiProviderOptions
{
    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    public string Model { get; set; } = "gemini-4-hydrogen";

    /// <summary>
    /// Gemini API key (required only when <c>AiProvider:Provider</c> is <c>"Gemini"</c>).
    /// Never commit real keys to appsettings*.json; inject via User Secrets or environment variables.
    /// </summary>
    public string? ApiKey { get; set; }

    public int TimeoutSeconds { get; set; } = 60;

    public int MaxRetries { get; set; } = 2;

    public int RetryBaseDelayMilliseconds { get; set; } = 500;
}
