using Microsoft.Extensions.Options;

namespace EduScroll.Infrastructure.Configuration;

public sealed class YouTubeOptionsValidator : IValidateOptions<YouTubeOptions>
{
    public ValidateOptionsResult Validate(string? name, YouTubeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (!IsValidHttpUrl(options.ApiBaseUrl))
        {
            failures.Add("YouTube:ApiBaseUrl must be a valid absolute HTTP or HTTPS URL.");
        }

        if (!IsValidHttpUrl(options.RssBaseUrl))
        {
            failures.Add("YouTube:RssBaseUrl must be a valid absolute HTTP or HTTPS URL.");
        }

        if (!string.IsNullOrWhiteSpace(options.ApiKey) &&
            options.ApiKey.Contains("REPLACE_WITH_", StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("YouTube:ApiKey contains an unreplaced placeholder value.");
        }

        if (options.TimeoutSeconds is < 1 or > 120)
        {
            failures.Add("YouTube:TimeoutSeconds must be between 1 and 120.");
        }

        if (options.MaxRetries is < 0 or > 5)
        {
            failures.Add("YouTube:MaxRetries must be between 0 and 5.");
        }

        if (options.RetryBaseDelayMilliseconds is < 0 or > 10_000)
        {
            failures.Add("YouTube:RetryBaseDelayMilliseconds must be between 0 and 10000.");
        }

        if (options.MetadataBatchSize is < 1 or > YouTubeOptions.MaxAllowedMetadataBatchSize)
        {
            failures.Add(
                $"YouTube:MetadataBatchSize must be between 1 and {YouTubeOptions.MaxAllowedMetadataBatchSize}.");
        }

        if (options.MaxVideosPerChannelPerSync is < 1 or > YouTubeOptions.MaxAllowedVideosPerChannelPerSync)
        {
            failures.Add(
                $"YouTube:MaxVideosPerChannelPerSync must be between 1 and {YouTubeOptions.MaxAllowedVideosPerChannelPerSync}.");
        }

        if (options.MinLocalCandidatesThreshold < 1)
        {
            failures.Add("YouTube:MinLocalCandidatesThreshold must be at least 1.");
        }

        if (options.MaxFallbackQueriesPerDiscovery is < 1 or > YouTubeOptions.MaxAllowedFallbackQueriesPerDiscovery)
        {
            failures.Add(
                $"YouTube:MaxFallbackQueriesPerDiscovery must be between 1 and {YouTubeOptions.MaxAllowedFallbackQueriesPerDiscovery}.");
        }

        if (options.MaxFallbackResultsPerQuery is < 1 or > YouTubeOptions.MaxAllowedFallbackResultsPerQuery)
        {
            failures.Add(
                $"YouTube:MaxFallbackResultsPerQuery must be between 1 and {YouTubeOptions.MaxAllowedFallbackResultsPerQuery}.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static bool IsValidHttpUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) &&
               (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
               !string.IsNullOrWhiteSpace(uri.Host);
    }
}
