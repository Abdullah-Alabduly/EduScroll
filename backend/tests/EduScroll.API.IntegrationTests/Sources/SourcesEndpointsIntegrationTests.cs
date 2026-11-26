using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using EduScroll.API.IntegrationTests.Infrastructure;
using EduScroll.Application.Authentication.Contracts;
using EduScroll.Application.Sources.Contracts;
using EduScroll.Infrastructure.WebExtraction;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EduScroll.API.IntegrationTests.Sources;

public sealed class SourcesEndpointsIntegrationTests(SqlServerApiFactory factory)
    : IClassFixture<SqlServerApiFactory>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly IPAddress PublicIp = IPAddress.Parse("93.184.216.34");
    private static readonly string SamplePdfText =
        "Cellular respiration converts biochemical energy from nutrients into adenosine triphosphate (ATP) and releases waste products.\n\n" +
        "Glycolysis occurs in the cytoplasm and breaks glucose into pyruvate molecules, producing a net gain of two ATP molecules.";

    [SkippableFact]
    public async Task AllSourceEndpoints_WhenUnauthenticated_ShouldReturn401()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        using var client = CreateClientWithMockedWeb();

        var randomId = Guid.NewGuid();
        var responses = new[]
        {
            await client.PostAsJsonAsync("/api/v1/sources", new CreatePdfSourceRequest("Doc", "Pdf", SamplePdfText)),
            await client.PostAsJsonAsync("/api/v1/sources/from-url", new CreateUrlSourceRequest("https://example.org/a")),
            await client.GetAsync("/api/v1/sources"),
            await client.GetAsync($"/api/v1/sources/{randomId}"),
            await client.DeleteAsync($"/api/v1/sources/{randomId}")
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            response.Dispose();
        }
    }

    [SkippableFact]
    public async Task PdfSourceLifecycle_CreateListDetailAndDelete_ShouldPersistAndWorkEndToEnd()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        using var client = CreateClientWithMockedWeb();
        var auth = await RegisterAndLoginAsync(client, "pdfowner");
        SetBearer(client, auth.AccessToken);

        // Even if a malicious client sends an extra ownerUserId field in JSON, it must be ignored.
        var payloadWithSpoofedOwner = new
        {
            ownerUserId = Guid.NewGuid(),
            title = "Biology Lecture 1",
            sourceType = "Pdf",
            extractedText = SamplePdfText,
            fileName = "biology_lecture_1.pdf"
        };

        using var createResponse = await client.PostAsJsonAsync("/api/v1/sources", payloadWithSpoofedOwner);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.NotNull(createResponse.Headers.Location);

        var created = await createResponse.Content.ReadFromJsonAsync<SourceDetailDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal("Biology Lecture 1", created!.Title);
        Assert.Equal("Pdf", created.SourceType);
        Assert.Equal("biology_lecture_1.pdf", created.OriginalFileName);
        Assert.Null(created.OriginalUrl);
        Assert.Equal("Completed", created.Status);
        Assert.Equal(SamplePdfText, created.ExtractedText);
        Assert.Equal(SamplePdfText.Length, created.ContentLength);

        // List sources returns summary without full extractedText property
        using var listResponse = await client.GetAsync("/api/v1/sources?page=1&pageSize=10");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var rawListJson = await listResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\"extractedText\"", rawListJson, StringComparison.OrdinalIgnoreCase);

        var paged = JsonSerializer.Deserialize<PagedSourcesResponse>(rawListJson, JsonOptions);
        Assert.NotNull(paged);
        Assert.Equal(1, paged!.TotalCount);
        Assert.Single(paged.Items);
        Assert.Equal(created.Id, paged.Items[0].Id);
        Assert.False(string.IsNullOrWhiteSpace(paged.Items[0].TextPreview));

        // Get detail returns full extractedText
        using var detailResponse = await client.GetAsync($"/api/v1/sources/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);

        var detail = await detailResponse.Content.ReadFromJsonAsync<SourceDetailDto>(JsonOptions);
        Assert.NotNull(detail);
        Assert.Equal(SamplePdfText, detail!.ExtractedText);

        // Delete removes the source
        using var deleteResponse = await client.DeleteAsync($"/api/v1/sources/{created.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        // Subsequent GET returns 404
        using var afterDeleteResponse = await client.GetAsync($"/api/v1/sources/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [SkippableFact]
    public async Task UrlSourceCreation_WithMockedPublicHtml_ShouldFetchExtractAndPersist()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        const string articleHtml = """
            <!doctype html>
            <html>
              <head>
                <title>Fallback Title</title>
                <meta property="og:title" content="Introduction to Thermodynamics" />
                <script>console.log('ignore me');</script>
              </head>
              <body>
                <nav><a href="/">Home</a></nav>
                <article>
                  <h1>Introduction to Thermodynamics</h1>
                  <p>The first law of thermodynamics states that energy cannot be created or destroyed, only transformed from one form to another.</p>
                  <p>Entropy measures the number of microscopic configurations corresponding to a thermodynamic system's macroscopic state.</p>
                </article>
                <footer>All rights reserved</footer>
              </body>
            </html>
            """;

        using var client = CreateClientWithMockedWeb(request =>
        {
            if (request.RequestUri?.AbsoluteUri == "https://learn.example.org/thermodynamics")
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(articleHtml, Encoding.UTF8, "text/html")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        });

        var auth = await RegisterAndLoginAsync(client, "urlowner");
        SetBearer(client, auth.AccessToken);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/sources/from-url",
            new CreateUrlSourceRequest("https://learn.example.org/thermodynamics"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<SourceDetailDto>(JsonOptions);
        Assert.NotNull(created);
        Assert.Equal("Introduction to Thermodynamics", created!.Title);
        Assert.Equal("WebArticle", created.SourceType);
        Assert.Equal("https://learn.example.org/thermodynamics", created.OriginalUrl);
        Assert.Contains("first law of thermodynamics", created.ExtractedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ignore me", created.ExtractedText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("All rights reserved", created.ExtractedText, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task CrossUserOwnership_UserBCannotListReadOrDeleteUserASource()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        using var clientA = CreateClientWithMockedWeb();
        var authA = await RegisterAndLoginAsync(clientA, "ownerA");
        SetBearer(clientA, authA.AccessToken);

        using var createResponse = await clientA.PostAsJsonAsync(
            "/api/v1/sources",
            new CreatePdfSourceRequest("User A Private Notes", "Pdf", SamplePdfText));
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var sourceA = await createResponse.Content.ReadFromJsonAsync<SourceDetailDto>(JsonOptions);
        Assert.NotNull(sourceA);

        using var clientB = CreateClientWithMockedWeb();
        var authB = await RegisterAndLoginAsync(clientB, "ownerB");
        SetBearer(clientB, authB.AccessToken);

        // User B list does not expose User A's source
        using var listBResponse = await clientB.GetAsync("/api/v1/sources");
        Assert.Equal(HttpStatusCode.OK, listBResponse.StatusCode);
        var listB = await listBResponse.Content.ReadFromJsonAsync<PagedSourcesResponse>(JsonOptions);
        Assert.NotNull(listB);
        Assert.Empty(listB!.Items);

        // User B cannot read User A's source by ID
        using var getBResponse = await clientB.GetAsync($"/api/v1/sources/{sourceA!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getBResponse.StatusCode);

        // User B cannot delete User A's source by ID
        using var deleteBResponse = await clientB.DeleteAsync($"/api/v1/sources/{sourceA.Id}");
        Assert.Equal(HttpStatusCode.NotFound, deleteBResponse.StatusCode);

        // User A's source must remain completely intact
        using var verifyAResponse = await clientA.GetAsync($"/api/v1/sources/{sourceA.Id}");
        Assert.Equal(HttpStatusCode.OK, verifyAResponse.StatusCode);
    }

    [SkippableTheory]
    [InlineData("http://127.0.0.1/admin", "URL_SSRF_BLOCKED")]
    [InlineData("http://localhost:5221/health/live", "URL_SSRF_BLOCKED")]
    [InlineData("http://169.254.169.254/latest/meta-data/", "URL_SSRF_BLOCKED")]
    [InlineData("http://192.168.1.1/router", "URL_SSRF_BLOCKED")]
    [InlineData("file:///etc/passwd", "URL_INVALID_SCHEME")]
    public async Task UrlSourceCreation_WithSsrfOrDisallowedUrl_ShouldReturn400WithoutLeakingInternalDetails(
        string maliciousUrl,
        string expectedCode)
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        using var client = CreateClientWithMockedWeb();
        var auth = await RegisterAndLoginAsync(client, "ssrfcheck");
        SetBearer(client, auth.AccessToken);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/sources/from-url",
            new CreateUrlSourceRequest(maliciousUrl));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal(expectedCode, doc.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("169.254.169.254", body, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
    }

    [SkippableFact]
    public async Task UrlSourceCreation_WhenPublicUrlRedirectsToPrivateIp_ShouldReturn400RedirectBlocked()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        using var client = CreateClientWithMockedWeb(_ =>
        {
            var redirect = new HttpResponseMessage(HttpStatusCode.Found);
            redirect.Headers.Location = new Uri("http://10.0.0.5/internal");
            return Task.FromResult(redirect);
        });

        var auth = await RegisterAndLoginAsync(client, "redirectcheck");
        SetBearer(client, auth.AccessToken);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/sources/from-url",
            new CreateUrlSourceRequest("https://learn.example.org/bounce"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        Assert.Equal("URL_REDIRECT_BLOCKED", doc.RootElement.GetProperty("code").GetString());
        Assert.DoesNotContain("10.0.0.5", body, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task PdfSourceCreation_WithInvalidInput_ShouldReturn400ValidationProblemDetails()
    {
        Skip.IfNot(factory.IsSqlServerAvailable, "SQL Server is not available.");

        using var client = CreateClientWithMockedWeb();
        var auth = await RegisterAndLoginAsync(client, "invalidpdf");
        SetBearer(client, auth.AccessToken);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/sources",
            new CreatePdfSourceRequest("", "Pdf", "too short"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("errors", out var errors));
        Assert.True(errors.TryGetProperty("title", out _));
        Assert.True(errors.TryGetProperty("extractedText", out _));
    }

    private HttpClient CreateClientWithMockedWeb(
        Func<HttpRequestMessage, Task<HttpResponseMessage>>? transportHandler = null)
    {
        var customFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IDnsResolver>();
                services.RemoveAll<IWebHttpTransport>();

                services.AddSingleton<IDnsResolver>(new StubDnsResolver(PublicIp));
                services.AddSingleton<IWebHttpTransport>(new StubWebHttpTransport(
                    transportHandler ?? (_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)))));
            });
        });

        return customFactory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
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

    private sealed class StubDnsResolver(IPAddress address) : IDnsResolver
    {
        public Task<IPAddress[]> GetHostAddressesAsync(string host, CancellationToken cancellationToken)
            => Task.FromResult(new[] { address });
    }

    private sealed class StubWebHttpTransport(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        : IWebHttpTransport
    {
        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => handler(request);
    }
}
