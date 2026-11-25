using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EduScroll.API.IntegrationTests.Infrastructure;
using EduScroll.API.Middleware;
using EduScroll.Application.Exceptions;
using EduScroll.Domain.Exceptions;
using EduScroll.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace EduScroll.API.IntegrationTests;

public sealed class ApiFoundationIntegrationTests : IClassFixture<ApiFoundationIntegrationTests.TestingApiFactory>
{
    private readonly TestingApiFactory _testingFactory;

    public ApiFoundationIntegrationTests(TestingApiFactory testingFactory)
    {
        _testingFactory = testingFactory;
    }

    [Fact]
    public async Task HealthLive_ShouldReturnHealthy200_WithoutSensitiveInfo()
    {
        using var client = _testingFactory.CreateClient();

        var response = await client.GetAsync("/health/live");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains(CorrelationIdMiddleware.HeaderName));

        using var json = JsonDocument.Parse(body);
        Assert.Equal("Healthy", json.RootElement.GetProperty("status").GetString());
        Assert.Single(json.RootElement.EnumerateObject());
    }

    [Fact]
    public async Task HealthReady_ShouldReturn503ProblemDetails_WhenDependencyIsUnhealthy()
    {
        using var factory = _testingFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddHealthChecks()
                    .AddCheck("simulated_unhealthy", () => HealthCheckResult.Unhealthy("Simulated failure"), tags: ["ready"]);
            });
        });
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(503, problem.Status);
        Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.6.4", problem.Type);
        Assert.DoesNotContain("Simulated failure", problem.Detail ?? string.Empty);
    }

    [Fact]
    public async Task CorrelationIdHeader_ShouldEchoValidClientValue_AndRejectInvalidValue()
    {
        using var client = _testingFactory.CreateClient();

        using var validRequest = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        validRequest.Headers.Add(CorrelationIdMiddleware.HeaderName, "req-test_123.abc");
        var validResponse = await client.SendAsync(validRequest);

        Assert.Equal(
            "req-test_123.abc",
            validResponse.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single());

        using var invalidRequest = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        invalidRequest.Headers.Add(CorrelationIdMiddleware.HeaderName, "bad value with spaces/slashes");
        var invalidResponse = await client.SendAsync(invalidRequest);
        var regenerated = invalidResponse.Headers.GetValues(CorrelationIdMiddleware.HeaderName).Single();

        Assert.NotEqual("bad value with spaces/slashes", regenerated);
        Assert.Equal(32, regenerated.Length);
    }

    [Fact]
    public async Task UnknownRoute_ShouldReturnStandardized404ProblemDetails()
    {
        using var client = _testingFactory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/non-existent-endpoint");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "corr-404-check");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(404, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("corr-404-check", json.RootElement.GetProperty("correlationId").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.RootElement.GetProperty("traceId").GetString()));
    }

    [Fact]
    public async Task OpenApiAndSwagger_ShouldBeExposedInDevelopment_AndHiddenInProductionAndTesting()
    {
        using var devFactory = new EnvironmentApiFactory(Environments.Development);
        using var devClient = devFactory.CreateClient();

        var openApiResponse = await devClient.GetAsync("/openapi/v1.json");
        var openApiBody = await openApiResponse.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, openApiResponse.StatusCode);
        Assert.Contains("EduScroll API", openApiBody);
        Assert.Contains("/health/live", openApiBody);
        Assert.Contains("/health/ready", openApiBody);
        Assert.DoesNotContain("_test/fault", openApiBody);

        using var openApiJson = JsonDocument.Parse(openApiBody);
        var problemProps = openApiJson.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .GetProperty("ProblemDetails")
            .GetProperty("properties");
        Assert.True(problemProps.TryGetProperty("code", out _));
        Assert.True(problemProps.TryGetProperty("traceId", out _));
        Assert.True(problemProps.TryGetProperty("correlationId", out _));

        var swaggerUiResponse = await devClient.GetAsync("/swagger/index.html");
        Assert.Equal(HttpStatusCode.OK, swaggerUiResponse.StatusCode);

        using var prodFactory = new EnvironmentApiFactory(Environments.Production);
        using var prodClient = prodFactory.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await prodClient.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await prodClient.GetAsync("/swagger/index.html")).StatusCode);

        using var testingClient = _testingFactory.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await testingClient.GetAsync("/openapi/v1.json")).StatusCode);
    }

    [Theory]
    [InlineData("validation", 400, "VALIDATION_FAILED", "https://tools.ietf.org/html/rfc9110#section-15.5.1")]
    [InlineData("domain", 422, "TEST_DOMAIN_ERROR", "https://tools.ietf.org/html/rfc9110#section-15.5.21")]
    [InlineData("notfound", 404, "SOURCE_NOT_FOUND", "https://tools.ietf.org/html/rfc9110#section-15.5.5")]
    [InlineData("conflict", 409, "DUPLICATE_ENTRY", "https://tools.ietf.org/html/rfc9110#section-15.5.10")]
    [InlineData("forbidden", 403, "ACCESS_FORBIDDEN", "https://tools.ietf.org/html/rfc9110#section-15.5.4")]
    [InlineData("external", 502, "UPSTREAM_UNAVAILABLE", "https://tools.ietf.org/html/rfc9110#section-15.6.3")]
    public async Task ExpectedExceptions_ShouldReturnMappedRfc9110ProblemDetails(
        string kind,
        int expectedStatus,
        string expectedCode,
        string expectedType)
    {
        using var factory = CreateFaultFactory(Environments.Production);
        using var client = factory.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/_test/fault/{kind}");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, "corr-expected-test");
        var response = await client.SendAsync(request);

        Assert.Equal((HttpStatusCode)expectedStatus, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(expectedStatus, json.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(expectedCode, json.RootElement.GetProperty("code").GetString());
        Assert.Equal(expectedType, json.RootElement.GetProperty("type").GetString());
        Assert.Equal("corr-expected-test", json.RootElement.GetProperty("correlationId").GetString());
        Assert.NotEqual("corr-expected-test", json.RootElement.GetProperty("traceId").GetString());
    }

    [Fact]
    public async Task UnexpectedException_ShouldHideDetailsInProduction_AndIncludeDetailsInDevelopment()
    {
        using var prodFactory = CreateFaultFactory(Environments.Production);
        using var prodClient = prodFactory.CreateClient();

        var prodResponse = await prodClient.GetAsync("/_test/fault/unexpected");
        var prodBody = await prodResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, prodResponse.StatusCode);
        Assert.DoesNotContain("sensitive internal diagnostic", prodBody);
        Assert.DoesNotContain("InvalidOperationException", prodBody);

        using var devFactory = CreateFaultFactory(Environments.Development);
        using var devClient = devFactory.CreateClient();

        var devResponse = await devClient.GetAsync("/_test/fault/unexpected");
        var devBody = await devResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.InternalServerError, devResponse.StatusCode);
        Assert.Contains("sensitive internal diagnostic", devBody);
        Assert.Contains("InvalidOperationException", devBody);
    }

    [Fact]
    public async Task Cors_ShouldAllowConfiguredOrigin_AndRejectUnlistedOrigin()
    {
        using var client = _testingFactory.CreateClient();

        using var allowedRequest = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        allowedRequest.Headers.Add("Origin", "https://test.eduscroll.local");
        var allowedResponse = await client.SendAsync(allowedRequest);

        Assert.True(allowedResponse.Headers.TryGetValues("Access-Control-Allow-Origin", out var allowedHeaders));
        Assert.Equal("https://test.eduscroll.local", allowedHeaders.Single());

        using var disallowedRequest = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        disallowedRequest.Headers.Add("Origin", "https://untrusted.example.com");
        var disallowedResponse = await client.SendAsync(disallowedRequest);

        Assert.False(disallowedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task AllowedHosts_ShouldRejectUntrustedHostHeader_InProduction()
    {
        using var prodFactory = new EnvironmentApiFactory(Environments.Production);
        using var prodClient = prodFactory.CreateClient();

        using var validHostReq = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        validHostReq.Headers.Host = "localhost";
        var validResp = await prodClient.SendAsync(validHostReq);
        Assert.Equal(HttpStatusCode.OK, validResp.StatusCode);

        using var invalidHostReq = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        invalidHostReq.Headers.Host = "evil.untrusted.example";
        var invalidResp = await prodClient.SendAsync(invalidHostReq);
        Assert.Equal(HttpStatusCode.BadRequest, invalidResp.StatusCode);
    }

    [Theory]
    [InlineData("*", false, false)]
    [InlineData("not-a-uri", false, false)]
    [InlineData("http://insecure.example.com", true, false)]
    [InlineData(null, true, true)]
    public void StartupConfiguration_ShouldFail_WhenCorsConfigIsInvalid(
        string? origin,
        bool productionMode,
        bool allowCredentials)
    {
        var env = productionMode ? Environments.Production : Environments.Development;
        var configValues = new Dictionary<string, string?>
        {
            ["HttpsRedirection:Enabled"] = "false",
            ["Cors:AllowCredentials"] = allowCredentials.ToString(),
            ["ConnectionStrings:DefaultConnection"] = "Server=(localdb)\\MSSQLLocalDB;Database=EduScroll_ConfigCheck;Trusted_Connection=True;",
            ["Jwt:Issuer"] = "EduScroll.API.Tests",
            ["Jwt:Audience"] = "EduScroll.TestClients",
            ["Jwt:SecretKey"] = SqlServerTestEnvironment.CreateEphemeralJwtSecret()
        };

        if (origin is not null)
        {
            configValues["Cors:AllowedOrigins:0"] = origin;
        }

        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(env);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(configValues);
            });
        });

        Assert.ThrowsAny<Exception>(() => factory.CreateClient());
    }

    [Fact]
    public async Task AddInfrastructure_ShouldValidateArgs_AndRegisterServicesIdempotently()
    {
        var config = new ConfigurationBuilder().Build();
        Assert.Throws<ArgumentNullException>(() => ((IServiceCollection)null!).AddInfrastructure(config));
        Assert.Throws<ArgumentNullException>(() => new ServiceCollection().AddInfrastructure(null!));

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(config);
        services.AddInfrastructure(config);

        Assert.Single(services, sd => sd.ServiceType == typeof(TimeProvider));

        using var provider = services.BuildServiceProvider();
        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());

        var schemeProvider = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        var schemes = await schemeProvider.GetAllSchemesAsync();
        Assert.Single(schemes, s => s.Name == "Bearer");
    }

    private static WebApplicationFactory<Program> CreateFaultFactory(string environmentName)
    {
        return new EnvironmentApiFactory(environmentName).WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddControllers()
                    .AddApplicationPart(typeof(TestFaultController).Assembly);
            });
        });
    }

    public sealed class TestingApiFactory : WebApplicationFactory<Program>
    {
        private readonly string _ephemeralSecret = SqlServerTestEnvironment.CreateEphemeralJwtSecret();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] =
                        "Server=(localdb)\\MSSQLLocalDB;Database=EduScroll_TestingPlaceholder;Trusted_Connection=True;Connect Timeout=1",
                    ["Jwt:SecretKey"] = _ephemeralSecret
                });
            });
        }
    }

    private sealed class EnvironmentApiFactory(string environmentName) : WebApplicationFactory<Program>
    {
        private readonly string _ephemeralSecret = SqlServerTestEnvironment.CreateEphemeralJwtSecret();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environmentName);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HttpsRedirection:Enabled"] = "false",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Server=(localdb)\\MSSQLLocalDB;Database=EduScroll_EnvPlaceholder;Trusted_Connection=True;Connect Timeout=1",
                    ["Jwt:SecretKey"] = _ephemeralSecret
                });
            });
        }
    }
}

[ApiController]
[Route("_test/fault")]
public sealed class TestFaultController : ControllerBase
{
    [HttpGet("validation")]
    public IActionResult ThrowValidation() =>
        throw new ValidationAppException(
            "Input failed validation.",
            new Dictionary<string, string[]> { ["url"] = ["Invalid scheme."] });

    [HttpGet("domain")]
    public IActionResult ThrowDomain() =>
        throw new TestFaultDomainException();

    [HttpGet("notfound")]
    public IActionResult ThrowNotFound() =>
        throw new NotFoundAppException("Source was not found.", "SOURCE_NOT_FOUND");

    [HttpGet("conflict")]
    public IActionResult ThrowConflict() =>
        throw new ConflictAppException("Duplicate entry.", "DUPLICATE_ENTRY");

    [HttpGet("forbidden")]
    public IActionResult ThrowForbidden() =>
        throw new AppException(AppErrorKind.Forbidden, "ACCESS_FORBIDDEN", "Access is denied.");

    [HttpGet("external")]
    public IActionResult ThrowExternal() =>
        throw new AppException(AppErrorKind.ExternalDependency, "UPSTREAM_UNAVAILABLE", "External service failed.");

    [HttpGet("unexpected")]
    public IActionResult ThrowUnexpected() =>
        throw new InvalidOperationException("sensitive internal diagnostic details");

    private sealed class TestFaultDomainException()
        : DomainException("TEST_DOMAIN_ERROR", "Domain rule failed.");
}
