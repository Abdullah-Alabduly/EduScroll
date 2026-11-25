using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduScroll.API.IntegrationTests.Infrastructure;
using EduScroll.API.Middleware;
using EduScroll.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EduScroll.API.IntegrationTests.Persistence;

public sealed class DatabaseMigrationAndReadinessTests : IClassFixture<SqlServerApiFactory>
{
    private readonly SqlServerApiFactory _factory;

    public DatabaseMigrationAndReadinessTests(SqlServerApiFactory factory)
    {
        _factory = factory;
    }

    [SkippableFact]
    public async Task Migrations_ShouldCreateIdentityAndRefreshTokenSchemaOnSqlServer()
    {
        SqlServerTestEnvironment.RequireAvailableSqlServer();

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync();
        Assert.Contains(appliedMigrations, m => m.EndsWith("InitialIdentityAndAuth", StringComparison.Ordinal));

        var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync();
        Assert.Empty(pendingMigrations);

        await using var connection = new SqlConnection(_factory.ConnectionString);
        await connection.OpenAsync();

        await using var tableCmd = connection.CreateCommand();
        tableCmd.CommandText = """
            SELECT TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_TYPE = 'BASE TABLE'
            """;

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await tableCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }
        }

        Assert.Contains("AspNetUsers", tables);
        Assert.Contains("AspNetRoles", tables);
        Assert.Contains("RefreshTokens", tables);

        await using var indexCmd = connection.CreateCommand();
        indexCmd.CommandText = """
            SELECT i.name, i.is_unique
            FROM sys.indexes i
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            WHERE t.name IN ('AspNetUsers', 'RefreshTokens') AND i.name IS NOT NULL
            """;

        var uniqueIndexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await indexCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                if (reader.GetBoolean(1))
                {
                    uniqueIndexes.Add(reader.GetString(0));
                }
            }
        }

        Assert.Contains("EmailIndex", uniqueIndexes);
        Assert.Contains("UserNameIndex", uniqueIndexes);
        Assert.Contains("IX_RefreshTokens_TokenHash", uniqueIndexes);
    }

    [SkippableFact]
    public async Task HealthReady_ShouldReturnHealthy200_WhenSqlServerDatabaseIsAvailable()
    {
        SqlServerTestEnvironment.RequireAvailableSqlServer();

        using var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.HeaderName));

        using var json = JsonDocument.Parse(body);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        Assert.Single(json.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task DatabaseOutage_ShouldKeepLivenessHealthy200_WhileReadinessReturns503()
    {
        var secret = SqlServerTestEnvironment.CreateEphemeralJwtSecret();
        using var outageFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        "Server=127.0.0.1,14339;Database=EduScroll_Unreachable;User Id=invalid;Password=invalid;Encrypt=False;Connect Timeout=1",
                    ["Jwt:SecretKey"] = secret
                });
            });
        });

        using var client = outageFactory.CreateClient();

        var liveResponse = await client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, liveResponse.StatusCode);

        var readyResponse = await client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, readyResponse.StatusCode);
        Assert.Equal("application/problem+json", readyResponse.Content.Headers.ContentType?.MediaType);

        var problem = await readyResponse.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(503, problem.Status);
        Assert.DoesNotContain("14339", problem.Detail ?? string.Empty);
        Assert.DoesNotContain("invalid", problem.Detail ?? string.Empty);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void StartupConfiguration_ShouldFail_WhenMandatoryDatabaseOrJwtSecretIsMissingInProduction(
        bool includeConnectionString,
        bool includeJwtSecret)
    {
        var values = new Dictionary<string, string?>
        {
            ["HttpsRedirection:Enabled"] = "false",
            ["ConnectionStrings:DefaultConnection"] = includeConnectionString
                ? "Server=localhost;Database=EduScrollDb;Trusted_Connection=True;"
                : "",
            ["Jwt:Issuer"] = "EduScroll.API",
            ["Jwt:Audience"] = "EduScroll.Clients",
            ["Jwt:SecretKey"] = includeJwtSecret
                ? SqlServerTestEnvironment.CreateEphemeralJwtSecret()
                : ""
        };

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Production);
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(values));
        });

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public void DesignTimeDbContextFactory_ShouldGiveEnvironmentVariablePrecedenceOverAppSettings()
    {
        const string envVar = "ConnectionStrings__DefaultConnection";
        const string overrideConn =
            "Server=(localdb)\\MSSQLLocalDB;Database=EduScroll_EnvOverrideCheck;Trusted_Connection=True;";
        var previousValue = Environment.GetEnvironmentVariable(envVar);

        try
        {
            Environment.SetEnvironmentVariable(envVar, overrideConn);
            var factory = new DesignTimeDbContextFactory();
            using var dbContext = factory.CreateDbContext([]);

            var actualBuilder = new SqlConnectionStringBuilder(dbContext.Database.GetConnectionString());
            Assert.Equal("(localdb)\\MSSQLLocalDB", actualBuilder.DataSource);
            Assert.Equal("EduScroll_EnvOverrideCheck", actualBuilder.InitialCatalog);
        }
        finally
        {
            Environment.SetEnvironmentVariable(envVar, previousValue);
        }
    }
}
