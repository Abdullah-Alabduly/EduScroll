using Microsoft.Extensions.Options;

namespace EduScroll.Infrastructure.Configuration;

public sealed class SourceIngestionOptionsValidator : IValidateOptions<SourceIngestionOptions>
{
    public ValidateOptionsResult Validate(string? name, SourceIngestionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.MinExtractedTextLength < 1)
        {
            failures.Add("SourceIngestion:MinExtractedTextLength must be at least 1.");
        }

        if (options.MaxExtractedTextLength < options.MinExtractedTextLength ||
            options.MaxExtractedTextLength > 1_000_000)
        {
            failures.Add(
                "SourceIngestion:MaxExtractedTextLength must be >= MinExtractedTextLength and <= 1,000,000.");
        }

        if (options.PreviewMaxLength is < 40 or > 1000)
        {
            failures.Add("SourceIngestion:PreviewMaxLength must be between 40 and 1000.");
        }

        if (options.DefaultPageSize < 1 || options.MaxPageSize < options.DefaultPageSize || options.MaxPageSize > 100)
        {
            failures.Add("SourceIngestion pagination limits must satisfy 1 <= DefaultPageSize <= MaxPageSize <= 100.");
        }

        if (options.WebFetch is null)
        {
            failures.Add("SourceIngestion:WebFetch section is required.");
        }
        else
        {
            if (options.WebFetch.ConnectTimeoutSeconds is < 1 or > 60)
            {
                failures.Add("SourceIngestion:WebFetch:ConnectTimeoutSeconds must be between 1 and 60.");
            }

            if (options.WebFetch.RequestTimeoutSeconds < options.WebFetch.ConnectTimeoutSeconds ||
                options.WebFetch.RequestTimeoutSeconds > 120)
            {
                failures.Add("SourceIngestion:WebFetch:RequestTimeoutSeconds must be >= ConnectTimeoutSeconds and <= 120.");
            }

            if (options.WebFetch.MaxResponseBytes is < 1024 or > 10 * 1024 * 1024)
            {
                failures.Add("SourceIngestion:WebFetch:MaxResponseBytes must be between 1 KB and 10 MB.");
            }

            if (options.WebFetch.MaxRedirects is < 0 or > 5)
            {
                failures.Add("SourceIngestion:WebFetch:MaxRedirects must be between 0 and 5.");
            }

            if (string.IsNullOrWhiteSpace(options.WebFetch.UserAgent))
            {
                failures.Add("SourceIngestion:WebFetch:UserAgent must not be empty.");
            }

            if (options.WebFetch.AllowedContentTypes is null || options.WebFetch.AllowedContentTypes.Length == 0)
            {
                failures.Add("SourceIngestion:WebFetch:AllowedContentTypes must specify at least one MIME type.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
