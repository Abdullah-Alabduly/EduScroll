using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace EduScroll.Infrastructure.Persistence;

/// <summary>
/// Design-time factory enabling EF Core CLI tooling (migrations, bundles, scripts)
/// without requiring runtime secret configuration.
/// Configuration precedence matches ASP.NET Core: appsettings -> User Secrets (Development) -> Environment Variables.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    private const string DesignTimeFallbackConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=EduScroll_Design;Trusted_Connection=True;TrustServerCertificate=True";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        var apiDirectory = ResolveApiProjectDirectory();

        var configBuilder = new ConfigurationBuilder();

        if (apiDirectory is not null)
        {
            configBuilder
                .SetBasePath(apiDirectory)
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true);

            if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
            {
                var userSecretsId = ReadUserSecretsId(Path.Combine(apiDirectory, "EduScroll.API.csproj"));
                if (!string.IsNullOrWhiteSpace(userSecretsId))
                {
                    configBuilder.AddUserSecrets(userSecretsId);
                }
            }
        }

        configBuilder.AddEnvironmentVariables();

        var configuration = configBuilder.Build();
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DesignTimeFallbackConnectionString;
        }

        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(connectionString, sqlOptions =>
        {
            sqlOptions.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
            sqlOptions.CommandTimeout(30);
        });

        return new ApplicationDbContext(optionsBuilder.Options);
    }

    private static string? ResolveApiProjectDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "src", "EduScroll.API"),
            Path.Combine(Directory.GetCurrentDirectory(), "..", "EduScroll.API"),
            Directory.GetCurrentDirectory()
        };

        return candidates.FirstOrDefault(dir =>
            Directory.Exists(dir) && File.Exists(Path.Combine(dir, "EduScroll.API.csproj")));
    }

    private static string? ReadUserSecretsId(string csprojPath)
    {
        try
        {
            var doc = XDocument.Load(csprojPath);
            return doc.Descendants("UserSecretsId").FirstOrDefault()?.Value.Trim();
        }
        catch
        {
            return null;
        }
    }
}
