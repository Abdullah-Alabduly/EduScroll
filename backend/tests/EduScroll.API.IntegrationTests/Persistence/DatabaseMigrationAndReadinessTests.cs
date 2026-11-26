using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduScroll.API.IntegrationTests.Infrastructure;
using EduScroll.API.Middleware;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Domain.Entities;
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
        Assert.Contains(appliedMigrations, m => m.EndsWith("AddSourcesTable", StringComparison.Ordinal));
        Assert.Contains(appliedMigrations, m => m.EndsWith("AddSourceAnalysisTables", StringComparison.Ordinal));
        Assert.Contains(appliedMigrations, m => m.EndsWith("AddMultiLanguageFeedQueries", StringComparison.Ordinal));
        Assert.Contains(appliedMigrations, m => m.EndsWith("AddVideoCatalogTables", StringComparison.Ordinal));

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
        Assert.Contains("Sources", tables);
        Assert.Contains("SourceAnalyses", tables);
        Assert.Contains("SourceTopics", tables);
        Assert.Contains("SourceSearchQueries", tables);
        Assert.Contains("WhitelistedChannels", tables);
        Assert.Contains("CatalogVideos", tables);

        await using var indexCmd = connection.CreateCommand();
        indexCmd.CommandText = """
            SELECT i.name, i.is_unique
            FROM sys.indexes i
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            WHERE t.name IN ('AspNetUsers', 'RefreshTokens', 'Sources', 'SourceAnalyses', 'SourceTopics', 'SourceSearchQueries', 'WhitelistedChannels', 'CatalogVideos') AND i.name IS NOT NULL
            """;

        var uniqueIndexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var allIndexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await indexCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                allIndexes.Add(name);
                if (reader.GetBoolean(1))
                {
                    uniqueIndexes.Add(name);
                }
            }
        }

        Assert.Contains("EmailIndex", uniqueIndexes);
        Assert.Contains("UserNameIndex", uniqueIndexes);
        Assert.Contains("IX_RefreshTokens_TokenHash", uniqueIndexes);
        Assert.Contains("IX_Sources_OwnerUserId_CreatedAtUtc", allIndexes);
        Assert.Contains("IX_SourceAnalyses_SourceId", uniqueIndexes);
        Assert.Contains("IX_SourceAnalyses_OwnerUserId_SourceId", allIndexes);
        Assert.Contains("IX_SourceTopics_SourceAnalysisId_DisplayOrder", allIndexes);
        Assert.Contains("IX_SourceSearchQueries_SourceAnalysisId_DisplayOrder", allIndexes);
        Assert.Contains("IX_WhitelistedChannels_YouTubeChannelId", uniqueIndexes);
        Assert.Contains("IX_CatalogVideos_YouTubeVideoId", uniqueIndexes);
        Assert.Contains("IX_CatalogVideos_Language_PublishedAtUtc", allIndexes);
    }

    [SkippableFact]
    public async Task VideoCatalogTables_ShouldEnforceUniqueConstraintsAndSupportRepositoryUpsertAndSearch()
    {
        SqlServerTestEnvironment.RequireAvailableSqlServer();

        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var enChannelExtId = $"UC_EN_{suffix}";
        var enVideoId = $"vidEn_{suffix}";
        var arVideoId = $"vidAr_{suffix}";

        using var scope = _factory.Services.CreateScope();
        var channelRepo = scope.ServiceProvider.GetRequiredService<IWhitelistedChannelRepository>();
        var videoRepo = scope.ServiceProvider.GetRequiredService<IVideoCatalogRepository>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // 1. Add whitelisted channel and verify unique constraint on YouTubeChannelId
        var enChannel = WhitelistedChannel.Create(enChannelExtId, "Test Math Channel", "en", "Mathematics", 0.95, now);
        await channelRepo.AddOrUpdateAsync(enChannel, CancellationToken.None);

        var activeEn = await channelRepo.GetActiveAsync("en", CancellationToken.None);
        Assert.Contains(activeEn, c => c.YouTubeChannelId == enChannelExtId);

        var duplicateChannel = WhitelistedChannel.Create(enChannelExtId, "Duplicate", "en", "Math", 0.5, now);
        dbContext.WhitelistedChannels.Add(duplicateChannel);
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        dbContext.Entry(duplicateChannel).State = EntityState.Detached;

        // 2. Upsert deduplicated catalog videos (including duplicate YouTubeVideoId in same batch)
        var v1Initial = CatalogVideo.Create(
            enVideoId,
            enChannel.Id,
            enChannelExtId,
            enChannel.Name,
            "Linear Algebra Eigenvalues Explained",
            "Clear explanation of eigenvectors and matrices.",
            "https://i.ytimg.com/vi/sample/hqdefault.jpg",
            "en",
            420,
            1000,
            100,
            now.AddHours(-2),
            DiscoverySource.WhitelistRss,
            now);

        var v1DuplicateInBatch = CatalogVideo.Create(
            enVideoId,
            enChannel.Id,
            enChannelExtId,
            enChannel.Name,
            "Linear Algebra Eigenvalues Explained (HD)",
            "Updated description on eigenvectors.",
            "https://i.ytimg.com/vi/sample/hqdefault.jpg",
            "en",
            420,
            2500,
            250,
            now.AddHours(-2),
            DiscoverySource.WhitelistRss,
            now.AddMinutes(5));

        var v2Arabic = CatalogVideo.Create(
            arVideoId,
            null,
            $"UC_AR_{suffix}",
            "قناة الفيزياء",
            "شرح القيم الذاتية في الجبر الخطي",
            "مقدمة مبسطة للمصفوفات.",
            null,
            "ar",
            null,
            500,
            40,
            now.AddHours(-1),
            DiscoverySource.YouTubeApiFallback,
            now);

        var affected = await videoRepo.UpsertRangeAsync([v1Initial, v1DuplicateInBatch, v2Arabic], CancellationToken.None);
        Assert.Equal(2, affected);

        var storedEn = await videoRepo.GetByYouTubeVideoIdAsync(enVideoId, CancellationToken.None);
        Assert.NotNull(storedEn);
        Assert.Equal("Linear Algebra Eigenvalues Explained (HD)", storedEn!.Title);
        Assert.Equal(2500, storedEn.ViewCount);

        // 3. Verify SQL Server unique constraint on CatalogVideos.YouTubeVideoId
        var duplicateVideo = CatalogVideo.Create(
            enVideoId,
            null,
            "UC_OTHER",
            "Other",
            "Duplicate Video",
            null,
            null,
            "en",
            60,
            1,
            0,
            now,
            DiscoverySource.YouTubeApiFallback,
            now);
        dbContext.CatalogVideos.Add(duplicateVideo);
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        dbContext.Entry(duplicateVideo).State = EntityState.Detached;

        // 4. Verify Database-First language-filtered search
        var enMatches = await videoRepo.SearchByLanguageAsync(["en"], ["Eigenvalues"], 10, CancellationToken.None);
        Assert.Contains(enMatches, v => v.YouTubeVideoId == enVideoId);
        Assert.DoesNotContain(enMatches, v => v.YouTubeVideoId == arVideoId);

        var arMatches = await videoRepo.SearchByLanguageAsync(["ar"], ["الجبر الخطي"], 10, CancellationToken.None);
        Assert.Contains(arMatches, v => v.YouTubeVideoId == arVideoId);
        Assert.DoesNotContain(arMatches, v => v.YouTubeVideoId == enVideoId);
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
