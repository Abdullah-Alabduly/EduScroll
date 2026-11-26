using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EduScroll.API.IntegrationTests.Infrastructure;
using EduScroll.Application.AiAnalysis.Contracts;
using EduScroll.Application.Authentication.Contracts;
using EduScroll.Application.Sources.Contracts;
using EduScroll.Infrastructure.AiAnalysis;
using EduScroll.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EduScroll.API.IntegrationTests.AiAnalysis;

public sealed class SourceAnalysisEndpointsIntegrationTests(SqlServerApiFactory factory)
    : IClassFixture<SqlServerApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly string SampleSourceText =
        "Mitosis is the process of eukaryotic cell division that produces two genetically identical daughter cells through prophase, metaphase, anaphase, and telophase.";

    private static readonly string ValidEnModelContentJson = """
        {
          "subject": "Cell Biology",
          "topic": "Mitosis",
          "summary": "Mitosis divides a eukaryotic cell's duplicated chromosomes equally into two genetically identical daughter cells through four ordered phases.",
          "topics": [
            {
              "name": "Mitosis",
              "subtopics": ["Prophase", "Metaphase", "Anaphase", "Telophase"]
            }
          ],
          "concepts": ["Chromosome Segregation", "Daughter Cells", "Spindle Fibers"],
          "keywords": ["mitosis", "cell division", "prophase", "metaphase"],
          "searchQueries": [
            { "query": "mitosis phases animated", "language": "en" },
            { "query": "how mitosis produces identical cells", "language": "en" },
            { "query": "prophase metaphase anaphase telophase explained", "language": "en" }
          ]
        }
        """;

    private static readonly string ValidArModelContentJson = """
        {
          "subject": "Cell Biology",
          "topic": "Mitosis",
          "summary": "Mitosis divides a eukaryotic cell's duplicated chromosomes equally into two genetically identical daughter cells through four ordered phases.",
          "topics": [
            {
              "name": "Mitosis",
              "subtopics": ["Prophase", "Metaphase", "Anaphase", "Telophase"]
            }
          ],
          "concepts": ["Chromosome Segregation", "Daughter Cells", "Spindle Fibers"],
          "keywords": ["mitosis", "cell division", "prophase", "metaphase"],
          "searchQueries": [
            { "query": "شرح مراحل الانقسام المتساوي للخلية", "language": "ar" },
            { "query": "الطور التمهيدي والاستوائي في الانقسام المتساوي", "language": "ar" },
            { "query": "الفرق بين الانقسام المتساوي والمنصف", "language": "ar" }
          ]
        }
        """;

    private static readonly string ValidBothModelContentJson = """
        {
          "subject": "Cell Biology",
          "topic": "Mitosis",
          "summary": "Mitosis divides a eukaryotic cell's duplicated chromosomes equally into two genetically identical daughter cells through four ordered phases.",
          "topics": [
            {
              "name": "Mitosis",
              "subtopics": ["Prophase", "Metaphase", "Anaphase", "Telophase"]
            }
          ],
          "concepts": ["Chromosome Segregation", "Daughter Cells", "Spindle Fibers"],
          "keywords": ["mitosis", "cell division", "prophase", "metaphase"],
          "searchQueries": [
            { "query": "mitosis phases animated", "language": "en" },
            { "query": "how mitosis produces identical cells", "language": "en" },
            { "query": "شرح مراحل الانقسام المتساوي للخلية", "language": "ar" },
            { "query": "الطور التمهيدي والاستوائي في الانقسام المتساوي", "language": "ar" }
          ]
        }
        """;

    [SkippableFact]
    public async Task AnalysisEndpoints_WhenUnauthenticated_ShouldReturn401()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        using var client = CreateClientWithMockedAi();
        var randomId = Guid.NewGuid();

        using var postResponse = await client.PostAsync($"/api/v1/sources/{randomId}/analyze", null);
        Assert.Equal(HttpStatusCode.Unauthorized, postResponse.StatusCode);

        using var getResponse = await client.GetAsync($"/api/v1/sources/{randomId}/analysis");
        Assert.Equal(HttpStatusCode.Unauthorized, getResponse.StatusCode);
    }

    [SkippableFact]
    public async Task AnalyzeAndRetrieve_MultiLanguagePersistenceCacheInvalidationAndCascadeDelete_ShouldWorkEndToEnd()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        var aiCalls = 0;
        using var customFactory = CreateFactoryWithMockedAi(async (req, _) =>
        {
            aiCalls++;
            var body = await req.Content!.ReadAsStringAsync();
            if (body.Contains("Requested Feed Query Language: Ar", StringComparison.Ordinal))
            {
                return BuildOllamaOkResponse(ValidArModelContentJson);
            }

            if (body.Contains("Requested Feed Query Language: Both", StringComparison.Ordinal))
            {
                return BuildOllamaOkResponse(ValidBothModelContentJson);
            }

            return BuildOllamaOkResponse(ValidEnModelContentJson);
        });
        using var client = customFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var auth = await RegisterAndLoginAsync(client, "aiowner");
        SetBearer(client, auth.AccessToken);

        var source = await CreatePdfSourceAsync(client, "Mitosis Lecture", SampleSourceText);

        // GET before analysis returns 404 SOURCE_ANALYSIS_NOT_FOUND
        using var preGet = await client.GetAsync($"/api/v1/sources/{source.Id}/analysis");
        Assert.Equal(HttpStatusCode.NotFound, preGet.StatusCode);

        // 1. POST /analyze with empty body defaults to TargetFeedLanguage="en"
        using var enResponse = await client.PostAsync($"/api/v1/sources/{source.Id}/analyze", null);
        Assert.Equal(HttpStatusCode.OK, enResponse.StatusCode);

        var rawEnJson = await enResponse.Content.ReadAsStringAsync();
        using (var doc = JsonDocument.Parse(rawEnJson))
        {
            Assert.Equal("en", doc.RootElement.GetProperty("targetFeedLanguage").GetString());
            var firstQuery = doc.RootElement.GetProperty("searchQueries")[0];
            Assert.True(firstQuery.TryGetProperty("query", out _));
            Assert.Equal("en", firstQuery.GetProperty("language").GetString());
        }

        var analyzedEn = JsonSerializer.Deserialize<SourceAnalysisDto>(rawEnJson, JsonOptions)!;
        Assert.Equal("en", analyzedEn.TargetFeedLanguage);
        Assert.Equal(3, analyzedEn.SearchQueries.Count);
        Assert.All(analyzedEn.SearchQueries, q => Assert.Equal("en", q.Language));
        Assert.Equal(1, aiCalls);

        // 2. Same TargetFeedLanguage="en" with forceRefresh=false uses persisted SQL Server cache
        using var enCachedResponse = await client.PostAsJsonAsync(
            $"/api/v1/sources/{source.Id}/analyze",
            new AnalyzeSourceRequest(ForceRefresh: false, TargetFeedLanguage: "en"));
        Assert.Equal(HttpStatusCode.OK, enCachedResponse.StatusCode);
        var cachedEn = await enCachedResponse.Content.ReadFromJsonAsync<SourceAnalysisDto>(JsonOptions);
        Assert.Equal(analyzedEn.Id, cachedEn!.Id);
        Assert.Equal(1, aiCalls);

        // 3. Different TargetFeedLanguage="ar" with forceRefresh=false MUST re-run & replace
        using var arResponse = await client.PostAsJsonAsync(
            $"/api/v1/sources/{source.Id}/analyze",
            new AnalyzeSourceRequest(ForceRefresh: false, TargetFeedLanguage: "ar"));
        Assert.Equal(HttpStatusCode.OK, arResponse.StatusCode);
        var analyzedAr = await arResponse.Content.ReadFromJsonAsync<SourceAnalysisDto>(JsonOptions);
        Assert.NotNull(analyzedAr);
        Assert.NotEqual(analyzedEn.Id, analyzedAr!.Id);
        Assert.Equal("ar", analyzedAr.TargetFeedLanguage);
        Assert.Equal(3, analyzedAr.SearchQueries.Count);
        Assert.All(analyzedAr.SearchQueries, q => Assert.Equal("ar", q.Language));
        Assert.Equal(2, aiCalls);

        // 4. Different TargetFeedLanguage="both" with forceRefresh=false MUST re-run & replace
        using var bothResponse = await client.PostAsJsonAsync(
            $"/api/v1/sources/{source.Id}/analyze",
            new AnalyzeSourceRequest(ForceRefresh: false, TargetFeedLanguage: "both"));
        Assert.Equal(HttpStatusCode.OK, bothResponse.StatusCode);
        var analyzedBoth = await bothResponse.Content.ReadFromJsonAsync<SourceAnalysisDto>(JsonOptions);
        Assert.NotNull(analyzedBoth);
        Assert.Equal("both", analyzedBoth!.TargetFeedLanguage);
        Assert.Equal(4, analyzedBoth.SearchQueries.Count);
        Assert.Equal(2, analyzedBoth.SearchQueries.Count(q => q.Language == "en"));
        Assert.Equal(2, analyzedBoth.SearchQueries.Count(q => q.Language == "ar"));
        Assert.Equal(3, aiCalls);

        // GET /analysis returns latest persisted dual-language record from SQL Server
        using var getResponse = await client.GetAsync($"/api/v1/sources/{source.Id}/analysis");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<SourceAnalysisDto>(JsonOptions);
        Assert.NotNull(fetched);
        Assert.Equal(analyzedBoth.Id, fetched!.Id);
        Assert.Equal("both", fetched.TargetFeedLanguage);
        Assert.Equal(4, fetched.SearchQueries.Count);

        // Deleting parent Source cascades cleanly on SQL Server
        using var deleteResponse = await client.DeleteAsync($"/api/v1/sources/{source.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        using var scope = customFactory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.False(await db.SourceAnalyses.AnyAsync(a => a.SourceId == source.Id));
        Assert.False(await db.SourceTopics.AnyAsync(t => t.SourceAnalysisId == analyzedBoth.Id));
        Assert.False(await db.SourceSearchQueries.AnyAsync(q => q.SourceAnalysisId == analyzedBoth.Id));
    }

    [SkippableFact]
    public async Task Analyze_WhenAiReturnsMismatchedLanguageTags_ShouldReturn422AndPersistNothing()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        var aiCalls = 0;
        using var customFactory = CreateFactoryWithMockedAi((_, _) =>
        {
            aiCalls++;
            // Always return Arabic queries even though "en" was requested
            return Task.FromResult(BuildOllamaOkResponse(ValidArModelContentJson));
        });
        using var client = customFactory.CreateClient();

        var auth = await RegisterAndLoginAsync(client, "mismatch");
        SetBearer(client, auth.AccessToken);

        var source = await CreatePdfSourceAsync(client, "Language Mismatch Test", SampleSourceText);

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/sources/{source.Id}/analyze",
            new AnalyzeSourceRequest(TargetFeedLanguage: "en"));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AI_SCHEMA_INVALID", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(2, aiCalls);

        using var getAfter = await client.GetAsync($"/api/v1/sources/{source.Id}/analysis");
        Assert.Equal(HttpStatusCode.NotFound, getAfter.StatusCode);
    }

    [SkippableFact]
    public async Task Analyze_WithInvalidTargetFeedLanguage_ShouldReturn400ValidationProblemDetails()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        using var client = CreateClientWithMockedAi();
        var auth = await RegisterAndLoginAsync(client, "badlang");
        SetBearer(client, auth.AccessToken);

        var source = await CreatePdfSourceAsync(client, "Invalid Lang Test", SampleSourceText);

        using var response = await client.PostAsJsonAsync(
            $"/api/v1/sources/{source.Id}/analyze",
            new AnalyzeSourceRequest(TargetFeedLanguage: "es"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AI_INVALID_TARGET_FEED_LANGUAGE", doc.RootElement.GetProperty("code").GetString());
    }

    [SkippableFact]
    public async Task Analyze_WhenAiReturnsMalformedJson_ShouldReturn422AndPersistNothing()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        var aiCalls = 0;
        using var customFactory = CreateFactoryWithMockedAi((_, _) =>
        {
            aiCalls++;
            return Task.FromResult(BuildOllamaOkResponse("{ broken json response"));
        });
        using var client = customFactory.CreateClient();

        var auth = await RegisterAndLoginAsync(client, "malformed");
        SetBearer(client, auth.AccessToken);

        var source = await CreatePdfSourceAsync(client, "Broken AI Test", SampleSourceText);

        using var response = await client.PostAsync($"/api/v1/sources/{source.Id}/analyze", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AI_SCHEMA_INVALID", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(2, aiCalls);

        using var getAfter = await client.GetAsync($"/api/v1/sources/{source.Id}/analysis");
        Assert.Equal(HttpStatusCode.NotFound, getAfter.StatusCode);
    }

    [SkippableFact]
    public async Task Analyze_WhenSourceTextExceedsAiMaxInputLimit_ShouldReturn422WithoutCallingProvider()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        var aiCalls = 0;
        using var customFactory = CreateFactoryWithMockedAi(
            (_, _) =>
            {
                aiCalls++;
                return Task.FromResult(BuildOllamaOkResponse(ValidEnModelContentJson));
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["AiProvider:MaxInputCharacters"] = "200"
            });
        using var client = customFactory.CreateClient();

        var auth = await RegisterAndLoginAsync(client, "oversized");
        SetBearer(client, auth.AccessToken);

        var largeText = new string('E', 500);
        var source = await CreatePdfSourceAsync(client, "Oversized Study Notes", largeText);

        using var response = await client.PostAsync($"/api/v1/sources/{source.Id}/analyze", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AI_INPUT_TOO_LONG", doc.RootElement.GetProperty("code").GetString());
        Assert.Equal(0, aiCalls);
    }

    [SkippableFact]
    public async Task Analyze_WhenProviderTimesOut_ShouldReturn504GatewayTimeout()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        using var customFactory = CreateFactoryWithMockedAi(
            async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(10), ct);
                return BuildOllamaOkResponse(ValidEnModelContentJson);
            },
            configOverrides: new Dictionary<string, string?>
            {
                ["AiProvider:TimeoutSeconds"] = "1",
                ["AiProvider:MaxRetries"] = "0"
            });
        using var client = customFactory.CreateClient();

        var auth = await RegisterAndLoginAsync(client, "timeout");
        SetBearer(client, auth.AccessToken);

        var source = await CreatePdfSourceAsync(client, "Timeout Notes", SampleSourceText);

        using var response = await client.PostAsync($"/api/v1/sources/{source.Id}/analyze", null);
        Assert.Equal(HttpStatusCode.GatewayTimeout, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AI_PROVIDER_TIMEOUT", doc.RootElement.GetProperty("code").GetString());
    }

    [SkippableFact]
    public async Task Analyze_WhenProviderReturns503_ShouldReturn502BadGateway()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        using var customFactory = CreateFactoryWithMockedAi(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            configOverrides: new Dictionary<string, string?>
            {
                ["AiProvider:MaxRetries"] = "1",
                ["AiProvider:RetryBaseDelayMilliseconds"] = "5"
            });
        using var client = customFactory.CreateClient();

        var auth = await RegisterAndLoginAsync(client, "unavailable");
        SetBearer(client, auth.AccessToken);

        var source = await CreatePdfSourceAsync(client, "Outage Notes", SampleSourceText);

        using var response = await client.PostAsync($"/api/v1/sources/{source.Id}/analyze", null);
        Assert.Equal(HttpStatusCode.BadGateway, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("AI_PROVIDER_UNAVAILABLE", doc.RootElement.GetProperty("code").GetString());
    }

    [SkippableFact]
    public async Task CrossUserOwnership_UserBCannotAnalyzeOrReadUserASourceAnalysis()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        var aiCalls = 0;
        using var customFactory = CreateFactoryWithMockedAi((_, _) =>
        {
            aiCalls++;
            return Task.FromResult(BuildOllamaOkResponse(ValidEnModelContentJson));
        });

        using var clientA = customFactory.CreateClient();
        var authA = await RegisterAndLoginAsync(clientA, "aowner");
        SetBearer(clientA, authA.AccessToken);
        var sourceA = await CreatePdfSourceAsync(clientA, "User A Secret Notes", SampleSourceText);

        using var clientB = customFactory.CreateClient();
        var authB = await RegisterAndLoginAsync(clientB, "bintruder");
        SetBearer(clientB, authB.AccessToken);

        using var bAnalyze = await clientB.PostAsync($"/api/v1/sources/{sourceA.Id}/analyze", null);
        Assert.Equal(HttpStatusCode.NotFound, bAnalyze.StatusCode);
        Assert.Equal(0, aiCalls);

        using var aAnalyze = await clientA.PostAsync($"/api/v1/sources/{sourceA.Id}/analyze", null);
        Assert.Equal(HttpStatusCode.OK, aAnalyze.StatusCode);
        Assert.Equal(1, aiCalls);

        using var bGet = await clientB.GetAsync($"/api/v1/sources/{sourceA.Id}/analysis");
        Assert.Equal(HttpStatusCode.NotFound, bGet.StatusCode);
    }

    private WebApplicationFactory<Program> CreateFactoryWithMockedAi(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? handler = null,
        Dictionary<string, string?>? configOverrides = null)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            if (configOverrides is not null)
            {
                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(configOverrides);
                });
            }

            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IAiHttpTransport>();
                services.AddSingleton<IAiHttpTransport>(new StubAiHttpTransport(
                    handler ?? ((_, _) => Task.FromResult(BuildOllamaOkResponse(ValidEnModelContentJson)))));
            });
        });
    }

    private HttpClient CreateClientWithMockedAi(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? handler = null)
    {
        return CreateFactoryWithMockedAi(handler).CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
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

    private sealed class StubAiHttpTransport(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler) : IAiHttpTransport
    {
        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request, cancellationToken);
    }
}
