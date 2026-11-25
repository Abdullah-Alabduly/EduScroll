using System.Security.Cryptography;
using EduScroll.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EduScroll.API.IntegrationTests.Infrastructure;

public static class SqlServerTestEnvironment
{
    public const string EnvVarName = "EDUSCROLL_TEST_SQLSERVER";

    private static readonly Lazy<string?> BaseConnectionString = new(DiscoverBaseConnectionString);

    public static string? GetBaseConnectionString() => BaseConnectionString.Value;

    public static string RequireAvailableSqlServer()
    {
        var conn = GetBaseConnectionString();
        Skip.If(
            conn is null,
            $"No reachable SQL Server instance detected. Ensure LocalDB '(localdb)\\MSSQLLocalDB' is installed or set '{EnvVarName}'.");
        return conn!;
    }

    public static string BuildDatabaseConnectionString(string baseConnectionString, string databaseName)
    {
        var builder = new SqlConnectionStringBuilder(baseConnectionString)
        {
            InitialCatalog = databaseName,
            ConnectTimeout = 10
        };
        return builder.ConnectionString;
    }

    public static string CreateEphemeralJwtSecret() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    private static string? DiscoverBaseConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable(EnvVarName);
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return CanOpenSqlConnection(configured) ? configured : null;
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        const string localDb =
            "Server=(localdb)\\MSSQLLocalDB;Database=master;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=5";

        return CanOpenSqlConnection(localDb) ? localDb : null;
    }

    private static bool CanOpenSqlConnection(string connectionString)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(connectionString)
            {
                InitialCatalog = "master",
                ConnectTimeout = 3
            };
            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            return true;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class SqlServerApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _databaseName = $"EduScroll_Test_{Guid.NewGuid():N}";
    private readonly string _ephemeralJwtSecret = SqlServerTestEnvironment.CreateEphemeralJwtSecret();
    private string? _connectionString;

    public bool IsSqlServerAvailable => _connectionString is not null;

    public string ConnectionString =>
        _connectionString ?? throw new InvalidOperationException("SQL Server is not available.");

    public async Task InitializeAsync()
    {
        var baseConn = SqlServerTestEnvironment.GetBaseConnectionString();
        if (baseConn is null)
        {
            return;
        }

        _connectionString = SqlServerTestEnvironment.BuildDatabaseConnectionString(baseConn, _databaseName);

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        if (_connectionString is not null)
        {
            try
            {
                using var scope = Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                await dbContext.Database.EnsureDeletedAsync();
            }
            catch
            {
                // Best-effort cleanup of ephemeral test database
            }
        }

        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) =>
        {
            var connectionString = _connectionString
                ?? "Server=(localdb)\\MSSQLLocalDB;Database=EduScroll_Unavailable;Trusted_Connection=True;TrustServerCertificate=True;Connect Timeout=1";

            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString,
                ["Jwt:Issuer"] = "EduScroll.API.Tests",
                ["Jwt:Audience"] = "EduScroll.TestClients",
                ["Jwt:SecretKey"] = _ephemeralJwtSecret,
                ["Jwt:AccessTokenExpirationMinutes"] = "15",
                ["Jwt:RefreshTokenExpirationDays"] = "7",
                ["Jwt:ClockSkewSeconds"] = "5"
            });
        });
    }
}
