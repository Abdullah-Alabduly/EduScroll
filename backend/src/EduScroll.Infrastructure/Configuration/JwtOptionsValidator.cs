using System.Text;
using Microsoft.Extensions.Options;

namespace EduScroll.Infrastructure.Configuration;

public sealed class JwtOptionsValidator : IValidateOptions<JwtOptions>
{
    public ValidateOptionsResult Validate(string? name, JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            failures.Add("Jwt:Issuer is required.");
        }

        if (string.IsNullOrWhiteSpace(options.Audience))
        {
            failures.Add("Jwt:Audience is required.");
        }

        if (string.IsNullOrWhiteSpace(options.SecretKey))
        {
            failures.Add(
                "Jwt:SecretKey is required. Configure a signing key via .NET User Secrets or environment variables.");
        }
        else
        {
            if (options.SecretKey.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase) ||
                options.SecretKey.Contains("YOUR_SECRET", StringComparison.OrdinalIgnoreCase))
            {
                failures.Add("Jwt:SecretKey contains an unconfigured placeholder value.");
            }

            if (Encoding.UTF8.GetByteCount(options.SecretKey) < JwtOptions.MinimumSecretKeyByteLength)
            {
                failures.Add(
                    $"Jwt:SecretKey must be at least {JwtOptions.MinimumSecretKeyByteLength} UTF-8 bytes (256 bits).");
            }
        }

        if (options.AccessTokenExpirationMinutes is < 1 or > 1440)
        {
            failures.Add("Jwt:AccessTokenExpirationMinutes must be between 1 and 1440.");
        }

        if (options.RefreshTokenExpirationDays is < 1 or > 90)
        {
            failures.Add("Jwt:RefreshTokenExpirationDays must be between 1 and 90.");
        }

        if (options.ClockSkewSeconds is < 0 or > 300)
        {
            failures.Add("Jwt:ClockSkewSeconds must be between 0 and 300.");
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}
