using EduScroll.Domain.Entities;
using Microsoft.Extensions.Options;

namespace EduScroll.Infrastructure.Configuration;

public sealed class AiProviderOptionsValidator : IValidateOptions<AiProviderOptions>
{
    private static readonly HashSet<string> SupportedProviders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ollama"
    };

    public ValidateOptionsResult Validate(string? name, AiProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Provider) || !SupportedProviders.Contains(options.Provider.Trim()))
        {
            failures.Add("AiProvider:Provider must be a supported provider ('Ollama').");
        }

        if (string.IsNullOrWhiteSpace(options.BaseUrl) ||
            !Uri.TryCreate(options.BaseUrl.Trim(), UriKind.Absolute, out var baseUri) ||
            (baseUri.Scheme != Uri.UriSchemeHttp && baseUri.Scheme != Uri.UriSchemeHttps) ||
            string.IsNullOrWhiteSpace(baseUri.Host))
        {
            failures.Add("AiProvider:BaseUrl must be a valid absolute HTTP or HTTPS URL.");
        }

        if (string.IsNullOrWhiteSpace(options.Model) || options.Model.Trim().Length > SourceAnalysis.MaxModelNameLength)
        {
            failures.Add($"AiProvider:Model is required and must not exceed {SourceAnalysis.MaxModelNameLength} characters.");
        }

        if (!string.IsNullOrWhiteSpace(options.ApiKey) &&
            options.ApiKey.Contains("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("AiProvider:ApiKey contains an unreplaced placeholder value.");
        }

        if (options.TimeoutSeconds is < 1 or > 300)
        {
            failures.Add("AiProvider:TimeoutSeconds must be between 1 and 300.");
        }

        if (options.MaxRetries is < 0 or > 5)
        {
            failures.Add("AiProvider:MaxRetries must be between 0 and 5.");
        }

        if (options.RetryBaseDelayMilliseconds is < 0 or > 10_000)
        {
            failures.Add("AiProvider:RetryBaseDelayMilliseconds must be between 0 and 10000.");
        }

        if (options.MinInputCharacters < 1)
        {
            failures.Add("AiProvider:MinInputCharacters must be at least 1.");
        }

        if (options.MaxInputCharacters < options.MinInputCharacters ||
            options.MaxInputCharacters > Source.DefaultMaxExtractedTextLength)
        {
            failures.Add(
                $"AiProvider:MaxInputCharacters must be >= MinInputCharacters and <= {Source.DefaultMaxExtractedTextLength}.");
        }

        if (options.MaxSchemaRepairAttempts is < 0 or > 2)
        {
            failures.Add("AiProvider:MaxSchemaRepairAttempts must be between 0 and 2.");
        }

        if (options.MaxSummaryLength is < 50 or > SourceAnalysis.MaxSummaryLength)
        {
            failures.Add($"AiProvider:MaxSummaryLength must be between 50 and {SourceAnalysis.MaxSummaryLength}.");
        }

        if (options.MaxTopics is < 1 or > SourceAnalysis.MaxTopics)
        {
            failures.Add($"AiProvider:MaxTopics must be between 1 and {SourceAnalysis.MaxTopics}.");
        }

        if (options.MaxSubtopicsPerTopic is < 0 or > SourceAnalysis.MaxSubtopicsPerTopic)
        {
            failures.Add($"AiProvider:MaxSubtopicsPerTopic must be between 0 and {SourceAnalysis.MaxSubtopicsPerTopic}.");
        }

        if (options.MaxConcepts is < 1 or > SourceAnalysis.MaxConcepts)
        {
            failures.Add($"AiProvider:MaxConcepts must be between 1 and {SourceAnalysis.MaxConcepts}.");
        }

        if (options.MaxKeywords is < 1 or > SourceAnalysis.MaxKeywords)
        {
            failures.Add($"AiProvider:MaxKeywords must be between 1 and {SourceAnalysis.MaxKeywords}.");
        }

        if (options.SingleLanguageMinSearchQueries < 1 ||
            options.SingleLanguageMaxSearchQueries < options.SingleLanguageMinSearchQueries ||
            options.SingleLanguageMaxSearchQueries > SourceAnalysis.MaxSearchQueries)
        {
            failures.Add(
                $"AiProvider single-language query bounds must satisfy 1 <= SingleLanguageMinSearchQueries <= SingleLanguageMaxSearchQueries <= {SourceAnalysis.MaxSearchQueries}.");
        }

        if (options.DualLanguageMinSearchQueriesPerLanguage < 1 ||
            options.DualLanguageMaxSearchQueriesPerLanguage < options.DualLanguageMinSearchQueriesPerLanguage ||
            options.DualLanguageMaxSearchQueriesPerLanguage * 2 > SourceAnalysis.MaxSearchQueries)
        {
            failures.Add(
                $"AiProvider dual-language query bounds must satisfy 1 <= DualLanguageMinSearchQueriesPerLanguage <= DualLanguageMaxSearchQueriesPerLanguage and total <= {SourceAnalysis.MaxSearchQueries}.");
        }

        if (options.Temperature is < 0.0 or > 1.0)
        {
            failures.Add("AiProvider:Temperature must be between 0.0 and 1.0.");
        }

        if (string.IsNullOrWhiteSpace(options.PromptVersion) ||
            options.PromptVersion.Trim().Length > SourceAnalysis.MaxPromptVersionLength)
        {
            failures.Add($"AiProvider:PromptVersion is required and must not exceed {SourceAnalysis.MaxPromptVersionLength} characters.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
