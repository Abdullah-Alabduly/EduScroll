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
        Assert.Contains("IX_WhitelistedChannels_IsActive_Language", allIndexes);
        Assert.Contains("IX_CatalogVideos_YouTubeVideoId", uniqueIndexes);
        Assert.Contains("IX_CatalogVideos_WhitelistedChannelId", allIndexes);
        Assert.Contains("IX_CatalogVideos_Language_PublishedAtUtc", allIndexes);

        await using var compositeIndexCmd = connection.CreateCommand();
        compositeIndexCmd.CommandText = """
            SELECT c.name, ic.is_descending_key
            FROM sys.indexes i
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
            INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
            WHERE t.name = 'CatalogVideos'
              AND i.name = 'IX_CatalogVideos_Language_PublishedAtUtc'
              AND ic.is_included_column = 0
            ORDER BY ic.key_ordinal
            """;

        var compositeColumns = new List<(string Column, bool IsDescending)>();
        await using (var reader = await compositeIndexCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                compositeColumns.Add((reader.GetString(0), reader.GetBoolean(1)));
            }
        }

        Assert.Equal(2, compositeColumns.Count);
        Assert.Equal(("Language", false), compositeColumns[0]);
        Assert.Equal(("PublishedAtUtc", true), compositeColumns[1]);

        await using var fkCmd = connection.CreateCommand();
        fkCmd.CommandText = """
            SELECT fk.delete_referential_action_desc
            FROM sys.foreign_keys fk
            INNER JOIN sys.tables t ON fk.parent_object_id = t.object_id
            WHERE t.name = 'CatalogVideos'
              AND fk.name = 'FK_CatalogVideos_WhitelistedChannels_WhitelistedChannelId'
            """;

        string? fkDeleteAction = null;
        await using (var reader = await fkCmd.ExecuteReaderAsync())
        {
            if (await reader.ReadAsync())
            {
                fkDeleteAction = reader.GetString(0);
            }
        }

        Assert.Equal("SET_NULL", fkDeleteAction);
    }

    [SkippableFact]
    public async Task VideoCatalogTables_ShouldEnforceUniqueConstraintsAndSupportRepositoryUpsertAndSearch()
    {
        SqlServerTestEnvironment.RequireAvailableSqlServer();

        var now = DateTimeOffset.UtcNow;
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var enChannelExtId = $"UC_EN_{suffix}";
        var inactiveArChannelExtId = $"UC_AR_OFF_{suffix}";
        var enVideoId = $"vidEn_{suffix}";
        var enOlderVideoId = $"vidEnOld_{suffix}";
        var arVideoId = $"vidAr_{suffix}";

        using var scope = _factory.Services.CreateScope();
        var channelRepo = scope.ServiceProvider.GetRequiredService<IWhitelistedChannelRepository>();
        var videoRepo = scope.ServiceProvider.GetRequiredService<IVideoCatalogRepository>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // 1. Add/update whitelisted channels, verify lookup/sync/active filtering & unique constraint
        var enChannel = WhitelistedChannel.Create(enChannelExtId, "Test Math Channel", "en", "Mathematics", 0.95, now);
        await channelRepo.AddOrUpdateAsync(enChannel, CancellationToken.None);

        var inactiveArChannel = WhitelistedChannel.Create(
            inactiveArChannelExtId,
            "Inactive Arabic Channel",
            "ar",
            "Physics",
            0.5,
            now,
            isActive: false);
        await channelRepo.AddOrUpdateAsync(inactiveArChannel, CancellationToken.None);

        var enChannelUpdate = WhitelistedChannel.Create(
            enChannelExtId,
            "Test Math Channel Updated",
            "en",
            "Linear Algebra",
            0.98,
            now.AddMinutes(1));
        await channelRepo.AddOrUpdateAsync(enChannelUpdate, CancellationToken.None);

        var syncedAtWithOffset = new DateTimeOffset(2026, 11, 26, 22, 0, 0, TimeSpan.FromHours(2));
        await channelRepo.MarkSyncedAsync(enChannel.Id, syncedAtWithOffset, CancellationToken.None);

        var channelById = await channelRepo.GetByIdAsync(enChannel.Id, CancellationToken.None);
        Assert.NotNull(channelById);
        Assert.Equal("Test Math Channel Updated", channelById!.Name);
        Assert.Equal("Linear Algebra", channelById.TopicCategory);
        Assert.Equal(0.98, channelById.TrustScore);
        Assert.NotNull(channelById.LastSyncedAtUtc);
        Assert.Equal(syncedAtWithOffset.ToUniversalTime(), channelById.LastSyncedAtUtc);
        Assert.Equal(TimeSpan.Zero, channelById.LastSyncedAtUtc!.Value.Offset);

        var channelByExternalId = await channelRepo.GetByYouTubeChannelIdAsync(enChannelExtId, CancellationToken.None);
        Assert.NotNull(channelByExternalId);
        Assert.Equal(enChannel.Id, channelByExternalId!.Id);

        var activeAll = await channelRepo.GetActiveAsync(null, CancellationToken.None);
        Assert.Contains(activeAll, c => c.YouTubeChannelId == enChannelExtId);
        Assert.DoesNotContain(activeAll, c => c.YouTubeChannelId == inactiveArChannelExtId);

        var activeEn = await channelRepo.GetActiveAsync("en", CancellationToken.None);
        Assert.Contains(activeEn, c => c.YouTubeChannelId == enChannelExtId);

        var duplicateChannel = WhitelistedChannel.Create(enChannelExtId, "Duplicate", "en", "Math", 0.5, now);
        dbContext.WhitelistedChannels.Add(duplicateChannel);
        await Assert.ThrowsAsync<DbUpdateException>(() => dbContext.SaveChangesAsync());
        dbContext.Entry(duplicateChannel).State = EntityState.Detached;

        // 2. Upsert deduplicated catalog videos (preserving richer batch fields + updating existing rows)
        var v1Initial = CatalogVideo.Create(
            enVideoId,
            enChannel.Id,
            enChannelExtId,
            channelById.Name,
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
            null,
            enChannelExtId,
            channelById.Name,
            "Linear Algebra Eigenvalues Explained (HD)",
            null,
            null,
            "en",
            null,
            2500,
            250,
            now.AddHours(-2),
            DiscoverySource.YouTubeApiFallback,
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

        var v3EnglishOlder = CatalogVideo.Create(
            enOlderVideoId,
            null,
            $"UC_EN_OLD_{suffix}",
            "Visual Math",
            "Eigenvalues in C# 100% Visual",
            "Older visual primer.",
            null,
            "en",
            180,
            800,
            50,
            now.AddDays(-5),
            DiscoverySource.YouTubeApiFallback,
            now);

        var affected = await videoRepo.UpsertRangeAsync(
            [v1Initial, v1DuplicateInBatch, v2Arabic, v3EnglishOlder],
            CancellationToken.None);
        Assert.Equal(3, affected);

        var storedEn = await videoRepo.GetByYouTubeVideoIdAsync(enVideoId, CancellationToken.None);
        Assert.NotNull(storedEn);
        Assert.Equal("Linear Algebra Eigenvalues Explained (HD)", storedEn!.Title);
        Assert.Equal("Clear explanation of eigenvectors and matrices.", storedEn.Description);
        Assert.Equal("https://i.ytimg.com/vi/sample/hqdefault.jpg", storedEn.ThumbnailUrl);
        Assert.Equal(enChannel.Id, storedEn.WhitelistedChannelId);
        Assert.Equal(420, storedEn.DurationSeconds);
        Assert.Equal(2500, storedEn.ViewCount);
        Assert.Equal(250, storedEn.LikeCount);
        Assert.Equal(DiscoverySource.WhitelistRss, storedEn.DiscoverySource);

        var batchLookup = await videoRepo.GetByYouTubeVideoIdsAsync(
            [enVideoId, arVideoId, "missing_video_id"],
            CancellationToken.None);
        Assert.Equal(2, batchLookup.Count);

        var v1PersistedRefresh = CatalogVideo.Create(
            enVideoId,
            null,
            enChannelExtId,
            channelById.Name,
            "Linear Algebra Eigenvalues Explained (HD)",
            null,
            null,
            "en",
            null,
            3000,
            300,
            now.AddHours(-2),
            DiscoverySource.YouTubeApiFallback,
            now.AddMinutes(10));
        var refreshedCount = await videoRepo.UpsertRangeAsync([v1PersistedRefresh], CancellationToken.None);
        Assert.Equal(1, refreshedCount);

        var refreshedEn = await videoRepo.GetByYouTubeVideoIdAsync(enVideoId, CancellationToken.None);
        Assert.NotNull(refreshedEn);
        Assert.Equal(3000, refreshedEn!.ViewCount);
        Assert.Equal(enChannel.Id, refreshedEn.WhitelistedChannelId);
        Assert.Equal(DiscoverySource.WhitelistRss, refreshedEn.DiscoverySource);

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

        // 4. Verify Database-First language-filtered search, newest-first order & literal term matching
        var enMatches = await videoRepo.SearchByLanguageAsync(["en"], ["Eigenvalues"], 10, CancellationToken.None);
        var enList = enMatches.ToList();
        var newerIdx = enList.FindIndex(v => v.YouTubeVideoId == enVideoId);
        var olderIdx = enList.FindIndex(v => v.YouTubeVideoId == enOlderVideoId);
        Assert.True(newerIdx >= 0 && olderIdx > newerIdx);
        Assert.DoesNotContain(enMatches, v => v.YouTubeVideoId == arVideoId);

        var literalPercentMatches = await videoRepo.SearchByLanguageAsync(["en"], ["100%"], 10, CancellationToken.None);
        Assert.Contains(literalPercentMatches, v => v.YouTubeVideoId == enOlderVideoId);
        Assert.DoesNotContain(literalPercentMatches, v => v.YouTubeVideoId == enVideoId);

        var enBrowse = await videoRepo.SearchByLanguageAsync(["en"], null, 10, CancellationToken.None);
        Assert.Contains(enBrowse, v => v.YouTubeVideoId == enVideoId);

        var arMatches = await videoRepo.SearchByLanguageAsync(["ar"], ["الجبر الخطي"], 10, CancellationToken.None);
        Assert.Contains(arMatches, v => v.YouTubeVideoId == arVideoId);
        Assert.DoesNotContain(arMatches, v => v.YouTubeVideoId == enVideoId);

        // 5. Verify ON DELETE SET NULL preserves CatalogVideos when WhitelistedChannel is deleted
        await dbContext.WhitelistedChannels
            .Where(c => c.Id == enChannel.Id)
            .ExecuteDeleteAsync();

        var afterChannelDelete = await videoRepo.GetByYouTubeVideoIdAsync(enVideoId, CancellationToken.None);
        Assert.NotNull(afterChannelDelete);
        Assert.Null(afterChannelDelete!.WhitelistedChannelId);
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
