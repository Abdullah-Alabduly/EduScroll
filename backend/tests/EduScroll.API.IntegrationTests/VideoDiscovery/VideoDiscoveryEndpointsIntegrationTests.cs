using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EduScroll.API.IntegrationTests.Infrastructure;
using EduScroll.Application.AiAnalysis.Contracts;
using EduScroll.Application.Authentication.Contracts;
using EduScroll.Application.Sources.Contracts;
using EduScroll.Application.VideoDiscovery.Contracts;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Domain.Entities;
using EduScroll.Infrastructure.AiAnalysis;
using EduScroll.Infrastructure.Persistence;
using EduScroll.Infrastructure.VideoDiscovery;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EduScroll.API.IntegrationTests.VideoDiscovery;

public sealed class VideoDiscoveryEndpointsIntegrationTests(SqlServerApiFactory factory)
    : IClassFixture<SqlServerApiFactory>
{
    private const string TestYouTubeApiKey = "test-integration-yt-key-999";
    private const string TestGeminiApiKey = "test-integration-gemini-secret-777";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string SampleSourceText =
        "Quantum entanglement and orbital astrophysics describe non-classical correlations and gravitational wave propagation across spacetime.";

    [SkippableFact]
    public async Task DiscoveryEndpoints_WhenUnauthenticated_ShouldReturn401Unauthorized()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        using var customFactory = CreateFactoryWithMockedDependencies();
        using var client = customFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        using var discoverResponse = await client.PostAsync($"/api/v1/sources/{Guid.NewGuid()}/discover", null);
        Assert.Equal(HttpStatusCode.Unauthorized, discoverResponse.StatusCode);

        using var matchResponse = await client.PostAsync($"/api/v1/sources/{Guid.NewGuid()}/match-videos", null);
        Assert.Equal(HttpStatusCode.Unauthorized, matchResponse.StatusCode);

        using var listChannelsResponse = await client.GetAsync("/api/v1/discovery/channels");
        Assert.Equal(HttpStatusCode.Unauthorized, listChannelsResponse.StatusCode);

        using var createChannelResponse = await client.PostAsJsonAsync(
            "/api/v1/discovery/channels",
            new CreateWhitelistedChannelRequest("UC_UNAUTH", "Unauth Channel", "en", "Science"));
        Assert.Equal(HttpStatusCode.Unauthorized, createChannelResponse.StatusCode);

        using var syncResponse = await client.PostAsync("/api/v1/discovery/whitelist/sync", null);
        Assert.Equal(HttpStatusCode.Unauthorized, syncResponse.StatusCode);
    }

    [SkippableFact]
    public async Task DiscoverForSource_WhenCrossUserOrUnanalyzed_ShouldReturnExpectedProblemDetails()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        using var customFactory = CreateFactoryWithMockedDependencies();
        using var clientA = customFactory.CreateClient();
        var authA = await RegisterAndLoginAsync(clientA, "discowner");
        SetBearer(clientA, authA.AccessToken);

        var sourceA = await CreatePdfSourceAsync(clientA, "Unanalyzed Physics Notes", SampleSourceText);

        // Owner calling /discover before /analyze receives 409 Conflict with SOURCE_NOT_ANALYZED
        using var unanalyzedResponse = await clientA.PostAsync($"/api/v1/sources/{sourceA.Id}/discover", null);
        Assert.Equal(HttpStatusCode.Conflict, unanalyzedResponse.StatusCode);
        Assert.Equal("application/problem+json", unanalyzedResponse.Content.Headers.ContentType?.MediaType);

        var unanalyzedBody = await unanalyzedResponse.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(unanalyzedBody))
        {
            Assert.Equal("SOURCE_NOT_ANALYZED", doc.RootElement.GetProperty("code").GetString());
        }

        // Different user calling /discover receives 404 Not Found with SOURCE_NOT_FOUND
        using var clientB = customFactory.CreateClient();
        var authB = await RegisterAndLoginAsync(clientB, "discintruder");
        SetBearer(clientB, authB.AccessToken);

        using var crossUserResponse = await clientB.PostAsync($"/api/v1/sources/{sourceA.Id}/discover", null);
        Assert.Equal(HttpStatusCode.NotFound, crossUserResponse.StatusCode);
        Assert.Equal("application/problem+json", crossUserResponse.Content.Headers.ContentType?.MediaType);

        var crossUserBody = await crossUserResponse.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(crossUserBody))
        {
            Assert.Equal("SOURCE_NOT_FOUND", doc.RootElement.GetProperty("code").GetString());
        }
    }

    [SkippableFact]
    public async Task WhitelistChannels_CreateListDuplicateAndValidationErrors_ShouldReturnExpectedResponses()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        using var customFactory = CreateFactoryWithMockedDependencies();
        using var client = customFactory.CreateClient();
        var auth = await RegisterAndLoginAsync(client, "chanadmin");
        SetBearer(client, auth.AccessToken);

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var enChannelId = $"UC_EN_{suffix}";
        var arChannelId = $"UC_AR_{suffix}";

        // 1. Create active EN channel -> 201 Created
        using var createEnResponse = await client.PostAsJsonAsync(
            "/api/v1/discovery/channels",
            new CreateWhitelistedChannelRequest(enChannelId, $"Math EN {suffix}", "EN", "Mathematics", 0.95, true));
        Assert.Equal(HttpStatusCode.Created, createEnResponse.StatusCode);

        var createdEn = await createEnResponse.Content.ReadFromJsonAsync<WhitelistedChannelDto>(JsonOptions);
        Assert.NotNull(createdEn);
        Assert.Equal(enChannelId, createdEn!.YouTubeChannelId);
        Assert.Equal("en", createdEn.Language);
        Assert.True(createdEn.IsActive);

        // 2. Duplicate YouTubeChannelId -> 409 Conflict ProblemDetails
        using var dupResponse = await client.PostAsJsonAsync(
            "/api/v1/discovery/channels",
            new CreateWhitelistedChannelRequest(enChannelId, "Duplicate Channel", "en", "Mathematics", 0.9, true));
        Assert.Equal(HttpStatusCode.Conflict, dupResponse.StatusCode);
        Assert.Equal("application/problem+json", dupResponse.Content.Headers.ContentType?.MediaType);

        using (var dupDoc = JsonDocument.Parse(await dupResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("CHANNEL_ALREADY_EXISTS", dupDoc.RootElement.GetProperty("code").GetString());
        }

        // 3. Domain validation errors -> 400 Bad Request ValidationProblemDetails
        using var badLangResponse = await client.PostAsJsonAsync(
            "/api/v1/discovery/channels",
            new CreateWhitelistedChannelRequest($"UC_BAD_{suffix}", "Bad Lang", "fr", "Physics", 0.8, true));
        Assert.Equal(HttpStatusCode.BadRequest, badLangResponse.StatusCode);
        Assert.Equal("application/problem+json", badLangResponse.Content.Headers.ContentType?.MediaType);

        using (var badLangDoc = JsonDocument.Parse(await badLangResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("CHANNEL_LANGUAGE_INVALID", badLangDoc.RootElement.GetProperty("code").GetString());
        }

        using var badTrustResponse = await client.PostAsJsonAsync(
            "/api/v1/discovery/channels",
            new CreateWhitelistedChannelRequest($"UC_TRUST_{suffix}", "Bad Trust", "en", "Physics", 1.5, true));
        Assert.Equal(HttpStatusCode.BadRequest, badTrustResponse.StatusCode);

        using (var badTrustDoc = JsonDocument.Parse(await badTrustResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("CHANNEL_TRUST_SCORE_INVALID", badTrustDoc.RootElement.GetProperty("code").GetString());
        }

        // 4. Create inactive AR channel and test filtering on GET /api/v1/discovery/channels
        using var createArResponse = await client.PostAsJsonAsync(
            "/api/v1/discovery/channels",
            new CreateWhitelistedChannelRequest(arChannelId, $"Physics AR {suffix}", "ar", "Physics", 0.88, false));
        Assert.Equal(HttpStatusCode.Created, createArResponse.StatusCode);

        using var listEnActive = await client.GetAsync("/api/v1/discovery/channels?language=en&isActive=true");
        Assert.Equal(HttpStatusCode.OK, listEnActive.StatusCode);
        var enChannels = await listEnActive.Content.ReadFromJsonAsync<List<WhitelistedChannelDto>>(JsonOptions);
        Assert.NotNull(enChannels);
        Assert.Contains(enChannels!, c => c.YouTubeChannelId == enChannelId);
        Assert.DoesNotContain(enChannels!, c => c.YouTubeChannelId == arChannelId);

        using var listArInactive = await client.GetAsync("/api/v1/discovery/channels?language=ar&isActive=false");
        Assert.Equal(HttpStatusCode.OK, listArInactive.StatusCode);
        var arChannels = await listArInactive.Content.ReadFromJsonAsync<List<WhitelistedChannelDto>>(JsonOptions);
        Assert.NotNull(arChannels);
        Assert.Contains(arChannels!, c => c.YouTubeChannelId == arChannelId);
        Assert.DoesNotContain(arChannels!, c => c.YouTubeChannelId == enChannelId);
    }

    [SkippableFact]
    public async Task WhitelistSync_And_DatabaseFirstDiscover_ShouldPersistToSqlServerAndSkipSearchFallback()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var topicToken = $"toksync{suffix}";
        var channelId = $"UC_SYNC_{suffix}";
        var videoId1 = $"rssVid1_{suffix}";
        var videoId2 = $"rssVid2_{suffix}";
        var searchCalls = 0;

        using var customFactory = CreateFactoryWithMockedDependencies(
            aiHandler: (_, _) => Task.FromResult(BuildOllamaOkResponse(BuildEnAnalysisJson(topicToken))),
            youtubeHandler: (req, _) =>
            {
                var path = req.RequestUri!.AbsolutePath;
                if (path.EndsWith("/feeds/videos.xml", StringComparison.OrdinalIgnoreCase))
                {
                    if (req.RequestUri.Query.Contains(channelId, StringComparison.Ordinal))
                    {
                        var atom = BuildAtomFeedXml(
                            channelId,
                            "Synced Edu Channel",
                            (videoId1, $"{topicToken} lesson one", "2026-11-25T10:00:00Z"),
                            (videoId2, $"{topicToken} lesson two", "2026-11-25T11:00:00Z"));
                        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new StringContent(atom, Encoding.UTF8, "application/atom+xml")
                        });
                    }

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(BuildEmptyAtomFeedXml(), Encoding.UTF8, "application/atom+xml")
                    });
                }

                if (path.EndsWith("/videos", StringComparison.OrdinalIgnoreCase))
                {
                    var ids = ParseQuery(req.RequestUri)["id"].Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var payload = JsonSerializer.Serialize(new
                    {
                        items = ids.Select(id => new
                        {
                            id,
                            snippet = new
                            {
                                channelId,
                                channelTitle = "Synced Edu Channel",
                                title = $"Enriched {topicToken} {id}",
                                description = $"Detailed {topicToken} description for {id}",
                                publishedAt = "2026-11-25T11:00:00Z",
                                defaultAudioLanguage = "en",
                                thumbnails = new
                                {
                                    high = new { url = $"https://i.ytimg.com/vi/{id}/hqdefault.jpg" }
                                }
                            },
                            contentDetails = new { duration = "PT2M15S" },
                            statistics = new { viewCount = "42000", likeCount = "1800" }
                        })
                    });

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    });
                }

                if (path.EndsWith("/search", StringComparison.OrdinalIgnoreCase))
                {
                    searchCalls++;
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("""{"items":[]}""", Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["YouTube:MinLocalCandidatesThreshold"] = "2",
                ["YouTube:EnableSearchFallback"] = "true"
            });

        using var client = customFactory.CreateClient();
        var auth = await RegisterAndLoginAsync(client, "syncuser");
        SetBearer(client, auth.AccessToken);

        // 1. Add whitelisted channel
        using var createChannelResponse = await client.PostAsJsonAsync(
            "/api/v1/discovery/channels",
            new CreateWhitelistedChannelRequest(channelId, "Synced Edu Channel", "en", "Physics", 0.95, true));
        Assert.Equal(HttpStatusCode.Created, createChannelResponse.StatusCode);

        // 2. Trigger whitelist sync -> persists RSS + videos.list enriched rows in SQL Server
        using var syncResponse = await client.PostAsJsonAsync(
            "/api/v1/discovery/whitelist/sync",
            new SyncWhitelistRequest("en"));
        Assert.Equal(HttpStatusCode.OK, syncResponse.StatusCode);

        var syncSummary = await syncResponse.Content.ReadFromJsonAsync<WhitelistSyncSummaryDto>(JsonOptions);
        Assert.NotNull(syncSummary);
        Assert.True(syncSummary!.SucceededChannelsCount >= 1);
        Assert.True(syncSummary.UpsertedVideosCount >= 2);

        using (var scope = customFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var v1 = await db.CatalogVideos.AsNoTracking().FirstOrDefaultAsync(v => v.YouTubeVideoId == videoId1);
            var v2 = await db.CatalogVideos.AsNoTracking().FirstOrDefaultAsync(v => v.YouTubeVideoId == videoId2);
            Assert.NotNull(v1);
            Assert.NotNull(v2);
            Assert.Equal(DiscoverySource.WhitelistRss, v1!.DiscoverySource);
            Assert.Equal(135, v1.DurationSeconds);
            Assert.Equal(42_000L, v1.ViewCount);
            Assert.Equal(1_800L, v1.LikeCount);

            var persistedChannel = await db.WhitelistedChannels.AsNoTracking()
                .FirstOrDefaultAsync(c => c.YouTubeChannelId == channelId);
            Assert.NotNull(persistedChannel);
            Assert.NotNull(persistedChannel!.LastSyncedAtUtc);
        }

        // 3. Create & analyze source -> call /discover -> hits local SQL Server catalog with FallbackTriggered = false
        var source = await CreatePdfSourceAsync(client, $"Lecture {topicToken}", SampleSourceText);
        using var analyzeResponse = await client.PostAsync($"/api/v1/sources/{source.Id}/analyze", null);
        Assert.Equal(HttpStatusCode.OK, analyzeResponse.StatusCode);

        using var discoverResponse = await client.PostAsJsonAsync(
            $"/api/v1/sources/{source.Id}/discover",
            new DiscoverSourceVideosRequest(MaxCandidates: 10));
        Assert.Equal(HttpStatusCode.OK, discoverResponse.StatusCode);

        var discoverBody = await discoverResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(TestYouTubeApiKey, discoverBody, StringComparison.Ordinal);

        var discovery = JsonSerializer.Deserialize<VideoDiscoveryResultDto>(discoverBody, JsonOptions);
        Assert.NotNull(discovery);
        Assert.False(discovery!.FallbackTriggered);
        Assert.Null(discovery.FallbackSkippedReason);
        Assert.Equal(2, discovery.LocalCandidateCount);
        Assert.Equal(0, discovery.FallbackCandidateCount);
        Assert.Equal(0, searchCalls);
        Assert.Contains(discovery.Candidates, c => c.YouTubeVideoId == videoId1);
        Assert.Contains(discovery.Candidates, c => c.YouTubeVideoId == videoId2);
    }

    [SkippableFact]
    public async Task DiscoverForSource_WhenLocalCatalogInsufficient_ShouldTriggerSearchFallbackWriteThroughCacheAndServeLocallyOnRepeat()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var topicToken = $"tokfb{suffix}";
        var fbVideoId1 = $"fbVid1_{suffix}";
        var fbVideoId2 = $"fbVid2_{suffix}";
        var searchCalls = 0;

        using var customFactory = CreateFactoryWithMockedDependencies(
            aiHandler: (_, _) => Task.FromResult(BuildOllamaOkResponse(BuildEnAnalysisJson(topicToken))),
            youtubeHandler: (req, _) =>
            {
                var path = req.RequestUri!.AbsolutePath;
                if (path.EndsWith("/search", StringComparison.OrdinalIgnoreCase))
                {
                    searchCalls++;
                    var payload = JsonSerializer.Serialize(new
                    {
                        items = new[]
                        {
                            new
                            {
                                id = new { kind = "youtube#video", videoId = fbVideoId1 },
                                snippet = new
                                {
                                    publishedAt = "2026-11-24T09:00:00Z",
                                    channelId = "UC_FB_CHAN",
                                    channelTitle = "Fallback Edu",
                                    title = $"{topicToken} visual guide",
                                    description = $"Visual guide for {topicToken}",
                                    thumbnails = new
                                    {
                                        high = new { url = $"https://i.ytimg.com/vi/{fbVideoId1}/hqdefault.jpg" }
                                    }
                                }
                            },
                            new
                            {
                                id = new { kind = "youtube#video", videoId = fbVideoId2 },
                                snippet = new
                                {
                                    publishedAt = "2026-11-24T10:00:00Z",
                                    channelId = "UC_FB_CHAN",
                                    channelTitle = "Fallback Edu",
                                    title = $"{topicToken} deep dive",
                                    description = $"Deep dive into {topicToken}",
                                    thumbnails = new
                                    {
                                        high = new { url = $"https://i.ytimg.com/vi/{fbVideoId2}/hqdefault.jpg" }
                                    }
                                }
                            }
                        }
                    });

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    });
                }

                if (path.EndsWith("/videos", StringComparison.OrdinalIgnoreCase))
                {
                    var ids = ParseQuery(req.RequestUri)["id"].Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var payload = JsonSerializer.Serialize(new
                    {
                        items = ids.Select(id => new
                        {
                            id,
                            snippet = new
                            {
                                channelId = "UC_FB_CHAN",
                                channelTitle = "Fallback Edu",
                                title = $"Enriched {topicToken} {id}",
                                description = $"Enriched {topicToken} explanation",
                                publishedAt = "2026-11-24T10:00:00Z",
                                defaultAudioLanguage = "en",
                                thumbnails = new
                                {
                                    high = new { url = $"https://i.ytimg.com/vi/{id}/hqdefault.jpg" }
                                }
                            },
                            contentDetails = new { duration = "PT1M30S" },
                            statistics = new { viewCount = "15500", likeCount = "910" }
                        })
                    });

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildEmptyAtomFeedXml(), Encoding.UTF8, "application/atom+xml")
                });
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["YouTube:MinLocalCandidatesThreshold"] = "2",
                ["YouTube:EnableSearchFallback"] = "true",
                ["YouTube:MaxFallbackQueriesPerDiscovery"] = "1",
                ["YouTube:MaxFallbackResultsPerQuery"] = "5"
            });

        using var client = customFactory.CreateClient();
        var auth = await RegisterAndLoginAsync(client, "fbuser");
        SetBearer(client, auth.AccessToken);

        var source = await CreatePdfSourceAsync(client, $"Fallback {topicToken}", SampleSourceText);
        using var analyzeResponse = await client.PostAsync($"/api/v1/sources/{source.Id}/analyze", null);
        Assert.Equal(HttpStatusCode.OK, analyzeResponse.StatusCode);

        // 1. First /discover: 0 local matches -> triggers search.list + videos.list + write-through SQL Server cache
        using var firstDiscover = await client.PostAsync($"/api/v1/sources/{source.Id}/discover", null);
        Assert.Equal(HttpStatusCode.OK, firstDiscover.StatusCode);

        var firstBody = await firstDiscover.Content.ReadAsStringAsync();
        Assert.DoesNotContain(TestYouTubeApiKey, firstBody, StringComparison.Ordinal);

        var firstResult = JsonSerializer.Deserialize<VideoDiscoveryResultDto>(firstBody, JsonOptions);
        Assert.NotNull(firstResult);
        Assert.True(firstResult!.FallbackTriggered);
        Assert.Null(firstResult.FallbackSkippedReason);
        Assert.Equal(0, firstResult.LocalCandidateCount);
        Assert.Equal(2, firstResult.FallbackCandidateCount);
        Assert.Equal(1, searchCalls);
        Assert.All(firstResult.Candidates, c =>
        {
            Assert.Equal(DiscoverySource.YouTubeApiFallback.ToString(), c.DiscoverySource);
            Assert.Equal(90, c.DurationSeconds);
            Assert.Equal(15_500L, c.ViewCount);
            Assert.Equal(910L, c.LikeCount);
        });

        // Verify persisted in SQL Server CatalogVideos
        using (var scope = customFactory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cached1 = await db.CatalogVideos.AsNoTracking().FirstOrDefaultAsync(v => v.YouTubeVideoId == fbVideoId1);
            var cached2 = await db.CatalogVideos.AsNoTracking().FirstOrDefaultAsync(v => v.YouTubeVideoId == fbVideoId2);
            Assert.NotNull(cached1);
            Assert.NotNull(cached2);
            Assert.Equal(DiscoverySource.YouTubeApiFallback, cached1!.DiscoverySource);
            Assert.Null(cached1.WhitelistedChannelId);
        }

        // 2. Second /discover: write-through cache now satisfies threshold locally (0 additional search.list calls)
        using var secondDiscover = await client.PostAsync($"/api/v1/sources/{source.Id}/discover", null);
        Assert.Equal(HttpStatusCode.OK, secondDiscover.StatusCode);

        var secondResult = await secondDiscover.Content.ReadFromJsonAsync<VideoDiscoveryResultDto>(JsonOptions);
        Assert.NotNull(secondResult);
        Assert.False(secondResult!.FallbackTriggered);
        Assert.Equal(2, secondResult.LocalCandidateCount);
        Assert.Equal(0, secondResult.FallbackCandidateCount);
        Assert.Equal(1, searchCalls);
    }

    [SkippableFact]
    public async Task SearchByLanguageAsync_OnSqlServer_ShouldIgnoreFooterBoilerplateEnforceTwoTokenDescriptionRuleAndOrderByRelevance()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var tokA = $"eigensql{suffix}";
        var tokB = $"vectorsql{suffix}";
        var now = DateTimeOffset.UtcNow;

        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IVideoCatalogRepository>();

        var footerOnlyRiemann = CatalogVideo.Create(
            $"sqlRiemann_{suffix}",
            null,
            "UC_3B1B",
            "3Blue1Brown",
            "We've been asking: What if AI solved the Riemann Hypothesis?",
            new string('x', 420) + $" made using a custom Python library, manim. video {tokA} {tokB}",
            null,
            "en",
            819,
            3_500_000,
            48_000,
            now,
            DiscoverySource.WhitelistRss,
            now);

        var singleDescTokenVideo = CatalogVideo.Create(
            $"sqlSingle_{suffix}",
            null,
            "UC_SINGLE",
            "Other Math",
            "Compression and Information Theory",
            $"Only mentions {tokA} once in the first 400 characters.",
            null,
            "en",
            1200,
            100_000,
            1_000,
            now.AddHours(-1),
            DiscoverySource.WhitelistRss,
            now);

        var twoDescTokenVideo = CatalogVideo.Create(
            $"sqlTwoDesc_{suffix}",
            null,
            "UC_DESC2",
            "Linear Channel",
            "Matrix Invariance Overview",
            $"Deep dive on {tokA} and {tokB} in the opening summary.",
            null,
            "en",
            600,
            50_000,
            500,
            now.AddHours(-2),
            DiscoverySource.WhitelistRss,
            now);

        var exactTitleMatchVideo = CatalogVideo.Create(
            $"sqlTitle_{suffix}",
            null,
            "UC_3B1B",
            "3Blue1Brown",
            $"Eigenvectors and {tokA} | Essence of {tokB}",
            "Visual chapter on eigenbases.",
            null,
            "en",
            1036,
            6_500_000,
            126_000,
            now.AddYears(-5),
            DiscoverySource.YouTubeApiFallback,
            now);

        var arTokA = $"الجبر{suffix}";
        var arTokB = $"الخطي{suffix}";
        var arUnrelated = CatalogVideo.Create(
            $"sqlArBad_{suffix}",
            null,
            "UC_AR",
            "Arabic Edu",
            "شرح فيديو عن الكيمياء",
            "مقدمة في الكيمياء",
            null,
            "ar",
            300,
            1000,
            50,
            now,
            DiscoverySource.WhitelistRss,
            now);

        var arMatch = CatalogVideo.Create(
            $"sqlArGood_{suffix}",
            null,
            "UC_AR",
            "Arabic Edu",
            $"أساسيات {arTokA} {arTokB}",
            "المصفوفات والتحويلات الخطية",
            null,
            "ar",
            450,
            2000,
            90,
            now.AddDays(-1),
            DiscoverySource.WhitelistRss,
            now);

        await repo.UpsertRangeAsync(
            [footerOnlyRiemann, singleDescTokenVideo, twoDescTokenVideo, exactTitleMatchVideo, arUnrelated, arMatch],
            CancellationToken.None);

        var enResults = await repo.SearchByLanguageAsync(["en"], [tokA, tokB], 10, CancellationToken.None);
        Assert.DoesNotContain(enResults, v => v.YouTubeVideoId == footerOnlyRiemann.YouTubeVideoId);
        Assert.DoesNotContain(enResults, v => v.YouTubeVideoId == singleDescTokenVideo.YouTubeVideoId);
        Assert.Equal(2, enResults.Count);
        Assert.Equal(exactTitleMatchVideo.YouTubeVideoId, enResults[0].YouTubeVideoId);
        Assert.Equal(twoDescTokenVideo.YouTubeVideoId, enResults[1].YouTubeVideoId);

        var arResults = await repo.SearchByLanguageAsync(["ar"], [arTokA, arTokB], 10, CancellationToken.None);
        var arOnly = Assert.Single(arResults);
        Assert.Equal(arMatch.YouTubeVideoId, arOnly.YouTubeVideoId);
    }

    [SkippableFact]
    public async Task MatchVideosForSource_WithGeminiProvider_ShouldAnalyzeUnanalyzedPdfReuseCacheAndReanalyzeWhenForced()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var topicToken = $"tokmatch{suffix}";
        var ytVideoId1 = $"matchVid1_{suffix}";
        var ytVideoId2 = $"matchVid2_{suffix}";
        var geminiCalls = 0;
        var searchCalls = 0;

        using var customFactory = CreateFactoryWithMockedGeminiAndYouTube(
            geminiHandler: (req, _) =>
            {
                geminiCalls++;
                Assert.True(req.Headers.TryGetValues("x-goog-api-key", out var apiKeyValues));
                Assert.Contains(TestGeminiApiKey, apiKeyValues);
                return Task.FromResult(BuildGeminiOkResponse(BuildEnAnalysisJson(topicToken)));
            },
            youtubeHandler: (req, _) =>
            {
                var path = req.RequestUri!.AbsolutePath;
                if (path.EndsWith("/search", StringComparison.OrdinalIgnoreCase))
                {
                    searchCalls++;
                    var payload = JsonSerializer.Serialize(new
                    {
                        items = new[]
                        {
                            new
                            {
                                id = new { kind = "youtube#video", videoId = ytVideoId1 },
                                snippet = new
                                {
                                    publishedAt = "2026-11-24T09:00:00Z",
                                    channelId = "UC_MATCH_CHAN",
                                    channelTitle = "Math Visuals",
                                    title = $"{topicToken} visual intuition",
                                    description = $"Geometric explanation of {topicToken}",
                                    thumbnails = new
                                    {
                                        high = new { url = $"https://i.ytimg.com/vi/{ytVideoId1}/hqdefault.jpg" }
                                    }
                                }
                            },
                            new
                            {
                                id = new { kind = "youtube#video", videoId = ytVideoId2 },
                                snippet = new
                                {
                                    publishedAt = "2026-11-24T10:00:00Z",
                                    channelId = "UC_MATCH_CHAN",
                                    channelTitle = "Math Visuals",
                                    title = $"{topicToken} worked examples",
                                    description = $"Worked problems covering {topicToken}",
                                    thumbnails = new
                                    {
                                        high = new { url = $"https://i.ytimg.com/vi/{ytVideoId2}/hqdefault.jpg" }
                                    }
                                }
                            }
                        }
                    });

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    });
                }

                if (path.EndsWith("/videos", StringComparison.OrdinalIgnoreCase))
                {
                    var ids = ParseQuery(req.RequestUri)["id"].Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var payload = JsonSerializer.Serialize(new
                    {
                        items = ids.Select(id => new
                        {
                            id,
                            snippet = new
                            {
                                channelId = "UC_MATCH_CHAN",
                                channelTitle = "Math Visuals",
                                title = $"Enriched {topicToken} {id}",
                                description = $"Detailed lecture on {topicToken}",
                                publishedAt = "2026-11-24T10:00:00Z",
                                defaultAudioLanguage = "en",
                                thumbnails = new
                                {
                                    high = new { url = $"https://i.ytimg.com/vi/{id}/hqdefault.jpg" }
                                }
                            },
                            contentDetails = new { duration = "PT3M20S" },
                            statistics = new { viewCount = "25000", likeCount = "1200" }
                        })
                    });

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildEmptyAtomFeedXml(), Encoding.UTF8, "application/atom+xml")
                });
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["YouTube:MinLocalCandidatesThreshold"] = "2",
                ["YouTube:EnableSearchFallback"] = "true",
                ["YouTube:MaxFallbackQueriesPerDiscovery"] = "1",
                ["YouTube:MaxFallbackResultsPerQuery"] = "5"
            });

        using var client = customFactory.CreateClient();
        var auth = await RegisterAndLoginAsync(client, "matchpdf");
        SetBearer(client, auth.AccessToken);

        // 1. Upload unanalyzed PDF source
        var source = await CreatePdfSourceAsync(client, $"Lecture {topicToken}", SampleSourceText);

        // 2. Call POST /match-videos -> automatically runs Gemini analysis + YouTube discovery
        using var firstResponse = await client.PostAsJsonAsync(
            $"/api/v1/sources/{source.Id}/match-videos",
            new MatchSourceVideosRequest(TargetFeedLanguage: "en", MaxCandidates: 5));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var firstBody = await firstResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(TestGeminiApiKey, firstBody, StringComparison.Ordinal);
        Assert.DoesNotContain(TestYouTubeApiKey, firstBody, StringComparison.Ordinal);

        var firstResult = JsonSerializer.Deserialize<SourceVideoMatchResultDto>(firstBody, JsonOptions);
        Assert.NotNull(firstResult);
        Assert.Equal(source.Id, firstResult!.SourceId);
        Assert.Equal("Gemini", firstResult.Provider);
        Assert.Equal("en", firstResult.TargetFeedLanguage);
        Assert.Equal(1, geminiCalls);
        Assert.Equal(1, searchCalls);
        Assert.True(firstResult.FallbackTriggered);
        Assert.Equal(2, firstResult.FallbackCandidateCount);
        Assert.Equal(2, firstResult.Candidates.Count);
        Assert.All(firstResult.Candidates, c =>
            Assert.Equal($"https://www.youtube.com/watch?v={c.YouTubeVideoId}", c.WatchUrl));

        // 3. Call POST /match-videos again with forceReanalyze = false -> reuses cached Gemini analysis + local write-through SQL Server cache
        using var secondResponse = await client.PostAsJsonAsync(
            $"/api/v1/sources/{source.Id}/match-videos",
            new MatchSourceVideosRequest(TargetFeedLanguage: "en", MaxCandidates: 5, ForceReanalyze: false));
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);

        var secondResult = await secondResponse.Content.ReadFromJsonAsync<SourceVideoMatchResultDto>(JsonOptions);
        Assert.NotNull(secondResult);
        Assert.Equal(firstResult.AnalysisId, secondResult!.AnalysisId);
        Assert.Equal(1, geminiCalls);
        Assert.Equal(1, searchCalls);
        Assert.False(secondResult.FallbackTriggered);
        Assert.Equal(2, secondResult.LocalCandidateCount);
        Assert.Equal(2, secondResult.Candidates.Count);
        Assert.All(secondResult.Candidates, c =>
            Assert.Equal($"https://www.youtube.com/watch?v={c.YouTubeVideoId}", c.WatchUrl));

        // 4. Call POST /match-videos with forceReanalyze = true -> re-runs Gemini
        using var forcedResponse = await client.PostAsJsonAsync(
            $"/api/v1/sources/{source.Id}/match-videos",
            new MatchSourceVideosRequest(TargetFeedLanguage: "en", MaxCandidates: 5, ForceReanalyze: true));
        Assert.Equal(HttpStatusCode.OK, forcedResponse.StatusCode);

        var forcedResult = await forcedResponse.Content.ReadFromJsonAsync<SourceVideoMatchResultDto>(JsonOptions);
        Assert.NotNull(forcedResult);
        Assert.Equal(2, geminiCalls);
        Assert.Equal("Gemini", forcedResult!.Provider);
        Assert.Equal(2, forcedResult.Candidates.Count);
    }

    [SkippableFact]
    public async Task MatchVideosForSource_WhenCrossUserInvalidRequestOrProviderUnavailable_ShouldReturnProblemDetailsWithoutLeakingSecrets()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        using var customFactory = CreateFactoryWithMockedGeminiAndYouTube(
            geminiHandler: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("upstream unavailable", Encoding.UTF8, "text/plain")
            }),
            youtubeHandler: (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"items":[]}""", Encoding.UTF8, "application/json")
            }));

        using var clientA = customFactory.CreateClient();
        var authA = await RegisterAndLoginAsync(clientA, "matcherr");
        SetBearer(clientA, authA.AccessToken);

        var sourceA = await CreatePdfSourceAsync(clientA, "Error Handling Source", SampleSourceText);

        // 1. Cross-user call -> 404 SOURCE_NOT_FOUND
        using var clientB = customFactory.CreateClient();
        var authB = await RegisterAndLoginAsync(clientB, "matchother");
        SetBearer(clientB, authB.AccessToken);

        using var crossUserResponse = await clientB.PostAsync($"/api/v1/sources/{sourceA.Id}/match-videos", null);
        Assert.Equal(HttpStatusCode.NotFound, crossUserResponse.StatusCode);
        Assert.Equal("application/problem+json", crossUserResponse.Content.Headers.ContentType?.MediaType);
        using (var doc = JsonDocument.Parse(await crossUserResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("SOURCE_NOT_FOUND", doc.RootElement.GetProperty("code").GetString());
        }

        // 2. Invalid maxCandidates -> 400 VIDEO_DISCOVERY_INVALID_CANDIDATE_COUNT
        foreach (var invalidCount in new[] { 0, 101 })
        {
            using var invalidCountResponse = await clientA.PostAsJsonAsync(
                $"/api/v1/sources/{sourceA.Id}/match-videos",
                new MatchSourceVideosRequest(MaxCandidates: invalidCount));
            Assert.Equal(HttpStatusCode.BadRequest, invalidCountResponse.StatusCode);
            Assert.Equal("application/problem+json", invalidCountResponse.Content.Headers.ContentType?.MediaType);
            using var doc = JsonDocument.Parse(await invalidCountResponse.Content.ReadAsStringAsync());
            Assert.Equal("VIDEO_DISCOVERY_INVALID_CANDIDATE_COUNT", doc.RootElement.GetProperty("code").GetString());
        }

        // 3. Invalid targetFeedLanguage -> 400 AI_INVALID_TARGET_FEED_LANGUAGE
        using var invalidLangResponse = await clientA.PostAsJsonAsync(
            $"/api/v1/sources/{sourceA.Id}/match-videos",
            new MatchSourceVideosRequest(TargetFeedLanguage: "fr", MaxCandidates: 5));
        Assert.Equal(HttpStatusCode.BadRequest, invalidLangResponse.StatusCode);
        Assert.Equal("application/problem+json", invalidLangResponse.Content.Headers.ContentType?.MediaType);
        using (var doc = JsonDocument.Parse(await invalidLangResponse.Content.ReadAsStringAsync()))
        {
            Assert.Equal("AI_INVALID_TARGET_FEED_LANGUAGE", doc.RootElement.GetProperty("code").GetString());
        }

        // 4. Gemini 503 -> 502 AI_PROVIDER_UNAVAILABLE without leaking API key
        using var upstreamResponse = await clientA.PostAsJsonAsync(
            $"/api/v1/sources/{sourceA.Id}/match-videos",
            new MatchSourceVideosRequest(TargetFeedLanguage: "en", MaxCandidates: 5));
        Assert.Equal(HttpStatusCode.BadGateway, upstreamResponse.StatusCode);
        Assert.Equal("application/problem+json", upstreamResponse.Content.Headers.ContentType?.MediaType);

        var upstreamBody = await upstreamResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(TestGeminiApiKey, upstreamBody, StringComparison.Ordinal);
        Assert.DoesNotContain(TestYouTubeApiKey, upstreamBody, StringComparison.Ordinal);
        using (var doc = JsonDocument.Parse(upstreamBody))
        {
            Assert.Equal("AI_PROVIDER_UNAVAILABLE", doc.RootElement.GetProperty("code").GetString());
        }
    }

    [SkippableFact]
    public async Task MatchVideosForSource_ShouldExcludeLongCatalogAndFallbackVideosAndSendVideoDurationShort()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        var suffix = Guid.NewGuid().ToString("N")[..8];
        var topicToken = $"tokdur{suffix}";
        var longLocalId = $"durLongLoc_{suffix}";
        var longFbId = $"durLongFb_{suffix}";
        var shortFbId = $"durShortFb_{suffix}";
        string? capturedSearchVideoDuration = null;
        var now = DateTimeOffset.UtcNow;

        // Seed a 45-minute (2700s) video matching the topic directly in SQL Server CatalogVideos
        using (var seedScope = factory.Services.CreateScope())
        {
            var repo = seedScope.ServiceProvider.GetRequiredService<IVideoCatalogRepository>();
            var longCatalogVideo = CatalogVideo.Create(
                longLocalId,
                null,
                "UC_LONG_MIT",
                "Long Lectures Channel",
                $"{topicToken} Complete 45-Minute University Lecture",
                $"Full lecture covering {topicToken}.",
                null,
                "en",
                durationSeconds: 2700,
                viewCount: 500_000,
                likeCount: 15_000,
                publishedAtUtc: now,
                discoverySource: DiscoverySource.WhitelistRss,
                utcNow: now);

            await repo.UpsertRangeAsync([longCatalogVideo], CancellationToken.None);

            // Direct SQL Server repository check with 240s cap must exclude the 2700s video
            var capped = await repo.SearchByLanguageAsync(["en"], [topicToken], 10, CancellationToken.None, maxDurationSeconds: 240);
            Assert.DoesNotContain(capped, v => v.YouTubeVideoId == longLocalId);
        }

        using var customFactory = CreateFactoryWithMockedGeminiAndYouTube(
            geminiHandler: (_, _) => Task.FromResult(BuildGeminiOkResponse(BuildEnAnalysisJson(topicToken))),
            youtubeHandler: (req, _) =>
            {
                var path = req.RequestUri!.AbsolutePath;
                if (path.EndsWith("/search", StringComparison.OrdinalIgnoreCase))
                {
                    var query = ParseQuery(req.RequestUri);
                    query.TryGetValue("videoDuration", out capturedSearchVideoDuration);

                    var payload = JsonSerializer.Serialize(new
                    {
                        items = new[]
                        {
                            new
                            {
                                id = new { kind = "youtube#video", videoId = longFbId },
                                snippet = new
                                {
                                    publishedAt = "2026-11-24T09:00:00Z",
                                    channelId = "UC_MIX",
                                    channelTitle = "Mixed Lengths",
                                    title = $"{topicToken} Full Hour Recording",
                                    description = $"Full lecture on {topicToken}",
                                    thumbnails = new { high = new { url = $"https://i.ytimg.com/vi/{longFbId}/hqdefault.jpg" } }
                                }
                            },
                            new
                            {
                                id = new { kind = "youtube#video", videoId = shortFbId },
                                snippet = new
                                {
                                    publishedAt = "2026-11-24T10:00:00Z",
                                    channelId = "UC_MIX",
                                    channelTitle = "Mixed Lengths",
                                    title = $"{topicToken} 3-Min Short Explained",
                                    description = $"Short overview of {topicToken}",
                                    thumbnails = new { high = new { url = $"https://i.ytimg.com/vi/{shortFbId}/hqdefault.jpg" } }
                                }
                            }
                        }
                    });

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    });
                }

                if (path.EndsWith("/videos", StringComparison.OrdinalIgnoreCase))
                {
                    var ids = ParseQuery(req.RequestUri)["id"].Split(',', StringSplitOptions.RemoveEmptyEntries);
                    var payload = JsonSerializer.Serialize(new
                    {
                        items = ids.Select(id => new
                        {
                            id,
                            snippet = new
                            {
                                channelId = "UC_MIX",
                                channelTitle = "Mixed Lengths",
                                title = $"Enriched {topicToken} {id}",
                                description = $"Overview of {topicToken}",
                                publishedAt = "2026-11-24T10:00:00Z",
                                defaultAudioLanguage = "en",
                                thumbnails = new { high = new { url = $"https://i.ytimg.com/vi/{id}/hqdefault.jpg" } }
                            },
                            contentDetails = new { duration = id == longFbId ? "PT45M" : "PT3M" },
                            statistics = new { viewCount = "12000", likeCount = "600" }
                        })
                    });

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildEmptyAtomFeedXml(), Encoding.UTF8, "application/atom+xml")
                });
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["YouTube:MinLocalCandidatesThreshold"] = "3",
                ["YouTube:EnableSearchFallback"] = "true",
                ["YouTube:SearchVideoDuration"] = "short",
                ["YouTube:MaxVideoDurationSeconds"] = "240"
            });

        using var client = customFactory.CreateClient();
        var auth = await RegisterAndLoginAsync(client, "shortonly");
        SetBearer(client, auth.AccessToken);

        var source = await CreatePdfSourceAsync(client, $"Short Video Filter {topicToken}", SampleSourceText);

        using var matchResponse = await client.PostAsJsonAsync(
            $"/api/v1/sources/{source.Id}/match-videos",
            new MatchSourceVideosRequest(TargetFeedLanguage: "en", MaxCandidates: 10));
        Assert.Equal(HttpStatusCode.OK, matchResponse.StatusCode);

        var result = await matchResponse.Content.ReadFromJsonAsync<SourceVideoMatchResultDto>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("short", capturedSearchVideoDuration);
        Assert.Equal(0, result!.LocalCandidateCount);
        Assert.Equal(1, result.FallbackCandidateCount);

        var only = Assert.Single(result.Candidates);
        Assert.Equal(shortFbId, only.YouTubeVideoId);
        Assert.Equal(180, only.DurationSeconds);
        Assert.Equal($"https://www.youtube.com/watch?v={shortFbId}", only.WatchUrl);
        Assert.DoesNotContain(result.Candidates, c => c.YouTubeVideoId == longLocalId || c.YouTubeVideoId == longFbId);
    }

    private WebApplicationFactory<Program> CreateFactoryWithMockedDependencies(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? aiHandler = null,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? youtubeHandler = null,
        Dictionary<string, string?>? configOverrides = null)
    {
        var effectiveAiHandler = aiHandler ?? ((_, _) => Task.FromResult(BuildOllamaOkResponse(BuildEnAnalysisJson("quantum"))));
        var effectiveYtHandler = youtubeHandler ?? ((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"items":[]}""", Encoding.UTF8, "application/json")
        }));

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var merged = new Dictionary<string, string?>
                {
                    ["YouTube:ApiKey"] = TestYouTubeApiKey,
                    ["YouTube:RetryBaseDelayMilliseconds"] = "5"
                };

                if (configOverrides is not null)
                {
                    foreach (var kvp in configOverrides)
                    {
                        merged[kvp.Key] = kvp.Value;
                    }
                }

                config.AddInMemoryCollection(merged);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAiHttpTransport>();
                services.AddSingleton<IAiHttpTransport>(new StubAiHttpTransport(effectiveAiHandler));

                services.RemoveAll<IYouTubeRssFeedClient>();
                services.AddHttpClient<IYouTubeRssFeedClient, YouTubeRssFeedClient>(c =>
                    {
                        c.Timeout = Timeout.InfiniteTimeSpan;
                    })
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(effectiveYtHandler));

                services.RemoveAll<IYouTubeVideoMetadataClient>();
                services.AddHttpClient<IYouTubeVideoMetadataClient, YouTubeVideoMetadataClient>(c =>
                    {
                        c.Timeout = Timeout.InfiniteTimeSpan;
                    })
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(effectiveYtHandler));

                services.RemoveAll<IYouTubeSearchFallbackClient>();
                services.AddHttpClient<IYouTubeSearchFallbackClient, YouTubeSearchFallbackClient>(c =>
                    {
                        c.Timeout = Timeout.InfiniteTimeSpan;
                    })
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(effectiveYtHandler));
            });
        });
    }

    private WebApplicationFactory<Program> CreateFactoryWithMockedGeminiAndYouTube(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> geminiHandler,
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? youtubeHandler = null,
        Dictionary<string, string?>? configOverrides = null)
    {
        var effectiveYtHandler = youtubeHandler ?? ((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"items":[]}""", Encoding.UTF8, "application/json")
        }));

        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var merged = new Dictionary<string, string?>
                {
                    ["AiProvider:Provider"] = "Gemini",
                    ["AiProvider:Gemini:ApiKey"] = TestGeminiApiKey,
                    ["AiProvider:Gemini:Model"] = "gemini-2.5-flash",
                    ["AiProvider:Gemini:RetryBaseDelayMilliseconds"] = "5",
                    ["YouTube:ApiKey"] = TestYouTubeApiKey,
                    ["YouTube:RetryBaseDelayMilliseconds"] = "5"
                };

                if (configOverrides is not null)
                {
                    foreach (var kvp in configOverrides)
                    {
                        merged[kvp.Key] = kvp.Value;
                    }
                }

                config.AddInMemoryCollection(merged);
            });

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<GeminiAiAnalysisProvider>();
                services.AddHttpClient<GeminiAiAnalysisProvider>(c =>
                    {
                        c.Timeout = Timeout.InfiniteTimeSpan;
                    })
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(geminiHandler));

                services.RemoveAll<IYouTubeRssFeedClient>();
                services.AddHttpClient<IYouTubeRssFeedClient, YouTubeRssFeedClient>(c =>
                    {
                        c.Timeout = Timeout.InfiniteTimeSpan;
                    })
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(effectiveYtHandler));

                services.RemoveAll<IYouTubeVideoMetadataClient>();
                services.AddHttpClient<IYouTubeVideoMetadataClient, YouTubeVideoMetadataClient>(c =>
                    {
                        c.Timeout = Timeout.InfiniteTimeSpan;
                    })
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(effectiveYtHandler));

                services.RemoveAll<IYouTubeSearchFallbackClient>();
                services.AddHttpClient<IYouTubeSearchFallbackClient, YouTubeSearchFallbackClient>(c =>
                    {
                        c.Timeout = Timeout.InfiniteTimeSpan;
                    })
                    .ConfigurePrimaryHttpMessageHandler(() => new StubHttpMessageHandler(effectiveYtHandler));
            });
        });
    }

    private static string BuildEnAnalysisJson(string topicToken) =>
        JsonSerializer.Serialize(new
        {
            subject = "Physics",
            topic = topicToken,
            summary = $"Concise academic overview covering {topicToken} principles and visual explanations.",
            topics = new[]
            {
                new { name = topicToken, subtopics = new[] { "Foundations", "Applications" } }
            },
            concepts = new[] { topicToken, $"{topicToken}mechanics" },
            keywords = new[] { topicToken, $"{topicToken}science" },
            searchQueries = new[]
            {
                new { query = $"{topicToken} {topicToken}alpha", language = "en" },
                new { query = $"{topicToken} {topicToken}beta", language = "en" },
                new { query = $"{topicToken} {topicToken}gamma", language = "en" }
            }
        });

    private static string BuildEmptyAtomFeedXml() =>
        """
        <?xml version="1.0" encoding="UTF-8"?>
        <feed xmlns:yt="http://www.youtube.com/xml/schemas/2015"
              xmlns:media="http://search.yahoo.com/mrss/"
              xmlns="http://www.w3.org/2005/Atom">
          <id>yt:channel:UC_empty</id>
          <title>Empty Channel</title>
        </feed>
        """;

    private static string BuildAtomFeedXml(
        string channelId,
        string channelTitle,
        params (string VideoId, string Title, string PublishedIso)[] entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        sb.AppendLine("""<feed xmlns:yt="http://www.youtube.com/xml/schemas/2015" xmlns:media="http://search.yahoo.com/mrss/" xmlns="http://www.w3.org/2005/Atom">""");
        sb.AppendLine($"  <id>yt:channel:{channelId}</id>");
        sb.AppendLine($"  <yt:channelId>{channelId}</yt:channelId>");
        sb.AppendLine($"  <title>{channelTitle}</title>");
        sb.AppendLine($"  <author><name>{channelTitle}</name></author>");

        foreach (var (videoId, title, publishedIso) in entries)
        {
            sb.AppendLine("  <entry>");
            sb.AppendLine($"    <id>yt:video:{videoId}</id>");
            sb.AppendLine($"    <yt:videoId>{videoId}</yt:videoId>");
            sb.AppendLine($"    <yt:channelId>{channelId}</yt:channelId>");
            sb.AppendLine($"    <title>{title}</title>");
            sb.AppendLine($"    <author><name>{channelTitle}</name></author>");
            sb.AppendLine($"    <published>{publishedIso}</published>");
            sb.AppendLine("    <media:group>");
            sb.AppendLine($"      <media:description>RSS description for {title}</media:description>");
            sb.AppendLine($"""      <media:thumbnail url="https://i.ytimg.com/vi/{videoId}/hqdefault.jpg" width="480" height="360"/>""");
            sb.AppendLine("""      <media:community><media:statistics views="1200"/></media:community>""");
            sb.AppendLine("    </media:group>");
            sb.AppendLine("  </entry>");
        }

        sb.AppendLine("</feed>");
        return sb.ToString();
    }

    private static HttpResponseMessage BuildOllamaOkResponse(string messageContent)
    {
        var envelope = JsonSerializer.Serialize(new
        {
            model = "llama3.2",
            message = new { role = "assistant", content = messageContent },
            done = true
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage BuildGeminiOkResponse(string messageContent)
    {
        var envelope = JsonSerializer.Serialize(new
        {
            candidates = new[]
            {
                new
                {
                    content = new
                    {
                        parts = new[]
                        {
                            new { text = messageContent }
                        }
                    }
                }
            }
        });

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(envelope, Encoding.UTF8, "application/json")
        };
    }

    private static async Task<SourceDetailDto> CreatePdfSourceAsync(
        HttpClient client,
        string title,
        string text)
    {
        using var createResponse = await client.PostAsJsonAsync(
            "/api/v1/sources",
            new CreatePdfSourceRequest(title, "Pdf", text, "notes.pdf"));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<SourceDetailDto>(JsonOptions);
        Assert.NotNull(created);
        return created!;
    }

    private static async Task<AuthResponse> RegisterAndLoginAsync(HttpClient client, string prefix)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var email = $"{prefix}_{suffix}@eduscroll.test";
        var userName = $"{prefix}_{suffix}";
        const string password = "StrongPassword123!";

        using var register = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest($"User {prefix}", userName, email, password));
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        using var login = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>(JsonOptions);
        Assert.NotNull(auth);
        return auth!;
    }

    private static void SetBearer(HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static Dictionary<string, string> ParseQuery(Uri uri)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var query = uri.Query.TrimStart('?');
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=', StringComparison.Ordinal);
            if (idx < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
                continue;
            }

            result[Uri.UnescapeDataString(pair[..idx])] = Uri.UnescapeDataString(pair[(idx + 1)..]);
        }

        return result;
    }

    private sealed class StubAiHttpTransport(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : IAiHttpTransport
    {
        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
