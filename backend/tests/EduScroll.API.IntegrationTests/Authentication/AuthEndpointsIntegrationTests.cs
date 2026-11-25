using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EduScroll.API.IntegrationTests.Infrastructure;
using EduScroll.Application.Authentication.Contracts;
using EduScroll.Infrastructure.Identity;
using EduScroll.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EduScroll.API.IntegrationTests.Authentication;

public sealed class AuthEndpointsIntegrationTests : IClassFixture<SqlServerApiFactory>
{
    private readonly SqlServerApiFactory _factory;

    public AuthEndpointsIntegrationTests(SqlServerApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task OpenApiDocument_InDevelopment_ShouldDocumentAuthEndpointsAndBearerSecurity()
    {
        var secret = SqlServerTestEnvironment.CreateEphemeralJwtSecret();
        using var devFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HttpsRedirection:Enabled"] = "false",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Server=(localdb)\\MSSQLLocalDB;Database=EduScroll_OpenApiCheck;Trusted_Connection=True;Connect Timeout=1",
                    ["Jwt:SecretKey"] = secret
                });
            });
        });

        using var client = devFactory.CreateClient();
        var response = await client.GetAsync("/openapi/v1.json");
        var jsonText = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(jsonText);
        var paths = doc.RootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/api/v1/auth/register", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/login", out var loginPath));
        Assert.True(paths.TryGetProperty("/api/v1/auth/refresh", out _));
        Assert.True(paths.TryGetProperty("/api/v1/auth/logout", out var logoutPath));
        Assert.True(paths.TryGetProperty("/api/v1/auth/me", out var mePath));

        Assert.False(loginPath.GetProperty("post").TryGetProperty("security", out _));
        Assert.True(logoutPath.GetProperty("post").TryGetProperty("security", out var logoutSecurity));
        Assert.True(mePath.GetProperty("get").TryGetProperty("security", out var meSecurity));

        Assert.True(logoutSecurity[0].TryGetProperty("Bearer", out _));
        Assert.True(meSecurity[0].TryGetProperty("Bearer", out _));

        var securitySchemes = doc.RootElement
            .GetProperty("components")
            .GetProperty("securitySchemes");
        Assert.True(securitySchemes.TryGetProperty("Bearer", out var bearerScheme));
        Assert.Equal("bearer", bearerScheme.GetProperty("scheme").GetString());
    }

    [SkippableFact]
    public async Task Register_ShouldPersistUserWithHashedPassword_AndRejectDuplicates()
    {
        SqlServerTestEnvironment.RequireAvailableSqlServer();
        using var client = _factory.CreateClient();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var email = $"learner.{suffix}@example.com";
        var userName = $"learner_{suffix}";
        const string password = "StrongPassword123!";

        var registerResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest("Ada Lovelace", userName, email, password));

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        var created = await registerResponse.Content.ReadFromJsonAsync<UserProfileDto>();
        Assert.NotNull(created);
        Assert.Equal("Ada Lovelace", created.FullName);
        Assert.Equal(userName, created.UserName);
        Assert.Equal(email, created.Email);
        Assert.True(created.IsActive);

        // Verify raw password is never stored in SQL Server
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var dbUser = await db.Users.SingleAsync(u => u.Id == created.Id);
            Assert.False(string.IsNullOrWhiteSpace(dbUser.PasswordHash));
            Assert.NotEqual(password, dbUser.PasswordHash);
        }

        // Duplicate email -> 409 Conflict
        var dupEmailResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest("Another Name", $"other_{suffix}", email.ToUpperInvariant(), password));
        Assert.Equal(HttpStatusCode.Conflict, dupEmailResponse.StatusCode);

        // Duplicate username -> 409 Conflict
        var dupUserResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest("Another Name", userName.ToUpperInvariant(), $"other.{suffix}@example.com", password));
        Assert.Equal(HttpStatusCode.Conflict, dupUserResponse.StatusCode);
    }

    [SkippableFact]
    public async Task Register_ShouldRejectInvalidInput_With400ValidationProblemDetails()
    {
        SqlServerTestEnvironment.RequireAvailableSqlServer();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest("", "x", "invalid-email", "weak"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [SkippableFact]
    public async Task LoginAndMe_ShouldIssueTokens_StoreOnlyTokenHash_AndProtectMeEndpoint()
    {
        SqlServerTestEnvironment.RequireAvailableSqlServer();
        using var client = _factory.CreateClient();

        // Anonymous /me must be rejected with 401 ProblemDetails
        var anonResponse = await client.GetAsync("/api/v1/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, anonResponse.StatusCode);
        Assert.Equal("application/problem+json", anonResponse.Content.Headers.ContentType?.MediaType);

        // Tampered Bearer token must be rejected with 401
        using (var tamperedReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me"))
        {
            tamperedReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.valid.jwt");
            var tamperedRes = await client.SendAsync(tamperedReq);
            Assert.Equal(HttpStatusCode.Unauthorized, tamperedRes.StatusCode);
        }

        var (email, password, registeredUser) = await RegisterTestUserAsync(client);

        // Wrong password -> 401
        var badLoginResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(email, "WrongPassword999!"));
        Assert.Equal(HttpStatusCode.Unauthorized, badLoginResponse.StatusCode);

        // Valid login -> 200 with JWT + refresh token
        var loginResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(email, password));
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        Assert.Equal("Bearer", auth.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
        Assert.Equal(registeredUser.Id, auth.User.Id);

        // Raw refresh token must NOT exist in SQL Server
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userTokens = await db.RefreshTokens
                .Where(rt => rt.UserId == registeredUser.Id)
                .ToListAsync();
            var storedToken = Assert.Single(userTokens);
            Assert.NotEqual(auth.RefreshToken, storedToken.TokenHash);
            Assert.Equal(64, storedToken.TokenHash.Length);
        }

        // Authenticated /me -> 200 without sensitive fields
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var meResponse = await client.SendAsync(meRequest);
        var meBody = await meResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        Assert.DoesNotContain("passwordHash", meBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("securityStamp", meBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("concurrencyStamp", meBody, StringComparison.OrdinalIgnoreCase);

        var profile = JsonSerializer.Deserialize<UserProfileDto>(
            meBody,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.NotNull(profile);
        Assert.Equal(registeredUser.Id, profile.Id);
        Assert.Equal(email, profile.Email);
    }

    [SkippableFact]
    public async Task Login_ShouldRejectInactiveAccount_AndLockOutAfterRepeatedFailures()
    {
        SqlServerTestEnvironment.RequireAvailableSqlServer();
        using var client = _factory.CreateClient();

        // Inactive user test
        var (inactiveEmail, password, inactiveUser) = await RegisterTestUserAsync(client);
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userEntity = await db.Users.SingleAsync(u => u.Id == inactiveUser.Id);
            userEntity.IsActive = false;
            await db.SaveChangesAsync();
        }

        var inactiveResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(inactiveEmail, password));
        Assert.Equal(HttpStatusCode.Forbidden, inactiveResponse.StatusCode);

        // Lockout test (5 wrong passwords -> 6th attempt with correct password is locked)
        var (lockEmail, lockPassword, _) = await RegisterTestUserAsync(client);
        for (var i = 0; i < 5; i++)
        {
            var failResponse = await client.PostAsJsonAsync(
                "/api/v1/auth/login",
                new LoginRequest(lockEmail, $"WrongPass{i}!"));
            Assert.True(failResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden);
        }

        var lockedResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(lockEmail, lockPassword));
        Assert.Equal(HttpStatusCode.Forbidden, lockedResponse.StatusCode);
    }

    [SkippableFact]
    public async Task Refresh_ShouldRotateToken_PreventReuse_AndHandleConcurrentRequestsSafely()
    {
        SqlServerTestEnvironment.RequireAvailableSqlServer();
        using var client = _factory.CreateClient();

        var (email, password, _) = await RegisterTestUserAsync(client);
        var loginResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(email, password));
        var initialAuth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(initialAuth);

        // Rotate R1 -> R2
        var refreshResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(initialAuth.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var rotatedAuth = await refreshResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(rotatedAuth);
        Assert.NotEqual(initialAuth.RefreshToken, rotatedAuth.RefreshToken);

        // Reuse R1 -> 401 Unauthorized
        var reuseResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(initialAuth.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);

        // Concurrent refresh attempts with R2 -> exactly one 200 OK and one 401 Unauthorized
        using var clientA = _factory.CreateClient();
        using var clientB = _factory.CreateClient();
        var taskA = clientA.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(rotatedAuth.RefreshToken));
        var taskB = clientB.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(rotatedAuth.RefreshToken));

        var responses = await Task.WhenAll(taskA, taskB);
        var statuses = responses.Select(r => r.StatusCode).OrderBy(s => (int)s).ToArray();
        Assert.Equal([HttpStatusCode.OK, HttpStatusCode.Unauthorized], statuses);
    }

    [SkippableFact]
    public async Task Refresh_ShouldRejectExpiredRefreshToken_With401()
    {
        SqlServerTestEnvironment.RequireAvailableSqlServer();
        using var client = _factory.CreateClient();

        var (email, password, user) = await RegisterTestUserAsync(client);
        var loginResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(email, password));
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        // Expire token directly in SQL Server
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var pastExpiry = DateTimeOffset.UtcNow.AddMinutes(-5);
            await db.RefreshTokens
                .Where(rt => rt.UserId == user.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(rt => rt.ExpiresAt, pastExpiry));
        }

        var refreshResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refreshResponse.StatusCode);
    }

    [SkippableFact]
    public async Task Logout_ShouldRequireAuth_RevokeRefreshToken_AndPreventSubsequentRefresh()
    {
        SqlServerTestEnvironment.RequireAvailableSqlServer();
        using var client = _factory.CreateClient();

        // Anonymous logout -> 401
        var anonLogout = await client.PostAsJsonAsync(
            "/api/v1/auth/logout",
            new LogoutRequest("some-token"));
        Assert.Equal(HttpStatusCode.Unauthorized, anonLogout.StatusCode);

        var (email, password, _) = await RegisterTestUserAsync(client);
        var loginResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(email, password));
        var auth = await loginResponse.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);

        using var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
        {
            Content = JsonContent.Create(new LogoutRequest(auth.RefreshToken))
        };
        logoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        var logoutResponse = await client.SendAsync(logoutReq);
        Assert.Equal(HttpStatusCode.OK, logoutResponse.StatusCode);
        var message = await logoutResponse.Content.ReadFromJsonAsync<MessageResponse>();
        Assert.NotNull(message);
        Assert.False(string.IsNullOrWhiteSpace(message.Message));

        // Refresh after logout -> 401
        var refreshAfterLogout = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(auth.RefreshToken));
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterLogout.StatusCode);
    }

    [Fact]
    public async Task ValidationErrors_ShouldReturnConsistentCamelCaseProblemDetails_ForMissingFieldsAndMalformedJson()
    {
        using var client = _factory.CreateClient();

        using var emptyObjResponse = await client.PostAsync(
            "/api/v1/auth/register",
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, emptyObjResponse.StatusCode);
        Assert.Equal("application/problem+json", emptyObjResponse.Content.Headers.ContentType?.MediaType);

        var emptyBody = await emptyObjResponse.Content.ReadAsStringAsync();
        using (var emptyJson = JsonDocument.Parse(emptyBody))
        {
            Assert.Equal("VALIDATION_FAILED", emptyJson.RootElement.GetProperty("code").GetString());
            Assert.Equal("/api/v1/auth/register", emptyJson.RootElement.GetProperty("instance").GetString());
            var errors = emptyJson.RootElement.GetProperty("errors");
            Assert.True(errors.TryGetProperty("email", out _));
            Assert.True(errors.TryGetProperty("fullName", out _));
            Assert.True(errors.TryGetProperty("userName", out _));
            Assert.True(errors.TryGetProperty("password", out _));
        }

        using var malformedResponse = await client.PostAsync(
            "/api/v1/auth/register",
            new StringContent("{bad-json", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);
        Assert.Equal("application/problem+json", malformedResponse.Content.Headers.ContentType?.MediaType);

        var malformedBody = await malformedResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain("BytePositionInLine", malformedBody);
        using var malformedJson = JsonDocument.Parse(malformedBody);
        Assert.Equal("VALIDATION_FAILED", malformedJson.RootElement.GetProperty("code").GetString());
    }

    [SkippableFact]
    public async Task MultiSessionUnicodeAndDeactivation_ShouldIsolateSessions_AndBlockDeactivatedUser()
    {
        SqlServerTestEnvironment.RequireAvailableSqlServer();
        using var client = _factory.CreateClient();

        var suffix = Guid.NewGuid().ToString("N")[..12];
        const string unicodeName = "José García — عباد";
        var email = $"unicode.{suffix}@example.com";
        var userName = $"uni_{suffix}";
        const string password = "StrongPassword123!";

        var regResponse = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest(unicodeName, userName, email, password));
        Assert.Equal(HttpStatusCode.Created, regResponse.StatusCode);
        var user = await regResponse.Content.ReadFromJsonAsync<UserProfileDto>();
        Assert.NotNull(user);
        Assert.Equal(unicodeName, user.FullName);

        // Login on two separate devices (including case-insensitive + whitespace-padded email)
        var device1Login = await (await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest($"  {email.ToUpperInvariant()}  ", password)))
            .Content.ReadFromJsonAsync<AuthResponse>();
        var device2Login = await (await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(email, password)))
            .Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(device1Login);
        Assert.NotNull(device2Login);

        // Logout Device 1 (and verify idempotent second logout)
        for (var i = 0; i < 2; i++)
        {
            using var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
            {
                Content = JsonContent.Create(new LogoutRequest(device1Login.RefreshToken))
            };
            logoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", device1Login.AccessToken);
            var logoutRes = await client.SendAsync(logoutReq);
            Assert.Equal(HttpStatusCode.OK, logoutRes.StatusCode);
        }

        // Device 1 token is revoked; Device 2 token still works
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshTokenRequest(device1Login.RefreshToken)))
            .StatusCode);

        var device2RefreshRes = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(device2Login.RefreshToken));
        Assert.Equal(HttpStatusCode.OK, device2RefreshRes.StatusCode);
        var device2Rotated = await device2RefreshRes.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(device2Rotated);

        // Another user cannot revoke User 1's active refresh token
        var (otherEmail, otherPassword, _) = await RegisterTestUserAsync(client);
        var otherLogin = await (await client.PostAsJsonAsync(
            "/api/v1/auth/login",
            new LoginRequest(otherEmail, otherPassword)))
            .Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(otherLogin);

        using (var crossLogoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout")
        {
            Content = JsonContent.Create(new LogoutRequest(device2Rotated.RefreshToken))
        })
        {
            crossLogoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherLogin.AccessToken);
            var crossLogoutRes = await client.SendAsync(crossLogoutReq);
            Assert.Equal(HttpStatusCode.Unauthorized, crossLogoutRes.StatusCode);
        }

        // Deactivate User 1 -> both /me and /refresh must reject with 403 Forbidden
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var entity = await db.Users.SingleAsync(u => u.Id == user.Id);
            entity.IsActive = false;
            await db.SaveChangesAsync();
        }

        using (var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/me"))
        {
            meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", device2Rotated.AccessToken);
            var meRes = await client.SendAsync(meReq);
            Assert.Equal(HttpStatusCode.Forbidden, meRes.StatusCode);
        }

        var inactiveRefreshRes = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new RefreshTokenRequest(device2Rotated.RefreshToken));
        Assert.Equal(HttpStatusCode.Forbidden, inactiveRefreshRes.StatusCode);
    }

    [Fact]
    public async Task CorsPreflight_OnAuthEndpoint_ShouldSucceedWithoutAuthentication()
    {
        using var client = _factory.CreateClient();
        using var preflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        preflight.Headers.Add("Origin", "https://test.eduscroll.local");
        preflight.Headers.Add("Access-Control-Request-Method", "POST");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type,authorization");

        var response = await client.SendAsync(preflight);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(
            "https://test.eduscroll.local",
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task CorsPreflight_InDevelopment_ShouldAllowDynamicLocalhostPort_AndRejectUntrustedOrigin()
    {
        var secret = SqlServerTestEnvironment.CreateEphemeralJwtSecret();
        using var devFactory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(Environments.Development);
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["HttpsRedirection:Enabled"] = "false",
                    ["ConnectionStrings:DefaultConnection"] =
                        "Server=(localdb)\\MSSQLLocalDB;Database=EduScroll_CorsDevCheck;Trusted_Connection=True;Connect Timeout=1",
                    ["Jwt:SecretKey"] = secret
                });
            });
        });

        using var client = devFactory.CreateClient();

        using var allowedPreflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        allowedPreflight.Headers.Add("Origin", "http://localhost:58392");
        allowedPreflight.Headers.Add("Access-Control-Request-Method", "POST");
        allowedPreflight.Headers.Add("Access-Control-Request-Headers", "content-type,x-correlation-id");

        var allowedResponse = await client.SendAsync(allowedPreflight);
        Assert.Equal(HttpStatusCode.NoContent, allowedResponse.StatusCode);
        Assert.Equal(
            "http://localhost:58392",
            allowedResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());

        using var blockedPreflight = new HttpRequestMessage(HttpMethod.Options, "/api/v1/auth/login");
        blockedPreflight.Headers.Add("Origin", "https://evil.example.com");
        blockedPreflight.Headers.Add("Access-Control-Request-Method", "POST");

        var blockedResponse = await client.SendAsync(blockedPreflight);
        Assert.False(blockedResponse.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static async Task<(string Email, string Password, UserProfileDto User)> RegisterTestUserAsync(
        HttpClient client)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var email = $"user.{suffix}@example.com";
        var userName = $"user_{suffix}";
        const string password = "StrongPassword123!";

        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/register",
            new RegisterRequest($"User {suffix}", userName, email, password));
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<UserProfileDto>();
        Assert.NotNull(created);
        return (email, password, created);
    }
}
