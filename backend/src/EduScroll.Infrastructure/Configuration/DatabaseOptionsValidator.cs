using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace EduScroll.Infrastructure.Configuration;

public sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.DefaultConnection))
        {
            return ValidateOptionsResult.Fail(
                "ConnectionStrings:DefaultConnection is required. Configure a SQL Server connection string via User Secrets, environment variables, or appsettings.");
        }

        if (options.DefaultConnection.Contains("YOUR_SQL_SERVER", StringComparison.OrdinalIgnoreCase) ||
            options.DefaultConnection.Contains("REPLACE_WITH", StringComparison.OrdinalIgnoreCase))
        {
            return ValidateOptionsResult.Fail(
                "ConnectionStrings:DefaultConnection contains an unconfigured placeholder value.");
        }

        try
        {
            var builder = new SqlConnectionStringBuilder(options.DefaultConnection);
            if (string.IsNullOrWhiteSpace(builder.DataSource))
            {
                return ValidateOptionsResult.Fail(
                    "ConnectionStrings:DefaultConnection must specify a SQL Server 'Server' or 'Data Source'.");
            }
        }
        catch (ArgumentException)
        {
            return ValidateOptionsResult.Fail(
                "ConnectionStrings:DefaultConnection is not a valid SQL Server connection string.");
        }

        return ValidateOptionsResult.Success;
    }
}
