using Microsoft.Extensions.Options;

namespace EduScroll.API.Configuration;

public sealed class CorsOptionsValidator(IHostEnvironment hostEnvironment)
    : IValidateOptions<CorsOptions>
{
    public ValidateOptionsResult Validate(string? name, CorsOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (options.AllowCredentials && options.AllowedOrigins.Length == 0)
        {
            failures.Add("Cors:AllowCredentials cannot be enabled when Cors:AllowedOrigins is empty.");
        }

        foreach (var origin in options.AllowedOrigins)
        {
            if (string.IsNullOrWhiteSpace(origin))
            {
                failures.Add("Cors:AllowedOrigins must not contain empty or whitespace entries.");
                continue;
            }

            if (origin.Trim() == "*")
            {
                failures.Add("Wildcard '*' CORS origins are prohibited. Specify explicit allowed origins.");
                continue;
            }

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                failures.Add($"Cors:AllowedOrigins contains an invalid origin URI: '{origin}'.");
                continue;
            }

            if (hostEnvironment.IsProduction() && uri.Scheme != Uri.UriSchemeHttps)
            {
                failures.Add($"Production CORS origin must use HTTPS: '{origin}'.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
