using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EduScroll.Application.Authentication.Contracts;
using EduScroll.Infrastructure.Authentication;
using EduScroll.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EduScroll.API.IntegrationTests.Security;

public sealed class JwtTokenServiceTests
{
    private const string TestSigningKey = "TestSigningKey_AtLeast32BytesLong_ForUnitTests_2026!";

    [Fact]
    public void GenerateAccessToken_ShouldProduceValidSignedJwt_WithExpectedClaimsAndLifetime()
    {
        var fixedNow = new DateTimeOffset(2026, 11, 17, 15, 0, 0, TimeSpan.Zero);
        var timeProvider = new FixedTimeProvider(fixedNow);
        var options = Options.Create(new JwtOptions
        {
            Issuer = "EduScroll.TestIssuer",
            Audience = "EduScroll.TestAudience",
            SecretKey = TestSigningKey,
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7,
            ClockSkewSeconds = 0
        });

        var sut = new JwtTokenService(options, timeProvider);
        var user = new UserProfileDto(
            Guid.NewGuid(),
            "Ada Lovelace",
            "ada.lovelace",
            "ada@example.com",
            fixedNow.AddDays(-1),
            fixedNow.AddDays(-1),
            true);

        var result = sut.GenerateAccessToken(user);

        Assert.False(string.IsNullOrWhiteSpace(result.Token));
        Assert.Equal(fixedNow.AddMinutes(15), result.ExpiresAtUtc);

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var validationParams = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)),
            ValidateIssuer = true,
            ValidIssuer = "EduScroll.TestIssuer",
            ValidateAudience = true,
            ValidAudience = "EduScroll.TestAudience",
            ValidateLifetime = false,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };

        var principal = handler.ValidateToken(result.Token, validationParams, out var validatedToken);
        var jwtToken = Assert.IsType<JwtSecurityToken>(validatedToken);

        Assert.Equal(SecurityAlgorithms.HmacSha256, jwtToken.Header.Alg);
        Assert.Equal(user.Id.ToString(), principal.FindFirstValue(JwtRegisteredClaimNames.Sub));
        Assert.Equal(user.Email, principal.FindFirstValue(JwtRegisteredClaimNames.Email));
        Assert.Equal(user.UserName, principal.FindFirstValue(JwtRegisteredClaimNames.Name));
        Assert.False(string.IsNullOrWhiteSpace(principal.FindFirstValue(JwtRegisteredClaimNames.Jti)));
        Assert.Equal(fixedNow.UtcDateTime, jwtToken.ValidFrom);
        Assert.Equal(fixedNow.AddMinutes(15).UtcDateTime, jwtToken.ValidTo);
    }

    [Fact]
    public void TokenValidation_ShouldRejectTamperedWrongIssuerWrongAudienceAndExpiredTokens()
    {
        var past = DateTimeOffset.UtcNow.AddMinutes(-30);
        var options = Options.Create(new JwtOptions
        {
            Issuer = "EduScroll.TestIssuer",
            Audience = "EduScroll.TestAudience",
            SecretKey = TestSigningKey,
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7,
            ClockSkewSeconds = 0
        });

        var sut = new JwtTokenService(options, new FixedTimeProvider(past));
        var user = new UserProfileDto(Guid.NewGuid(), "Test User", "testuser", "test@example.com", past, past, true);
        var expiredToken = sut.GenerateAccessToken(user).Token;

        var currentSut = new JwtTokenService(options, TimeProvider.System);
        var activeToken = currentSut.GenerateAccessToken(user).Token;

        var handler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var baseParams = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)),
            ValidateIssuer = true,
            ValidIssuer = "EduScroll.TestIssuer",
            ValidateAudience = true,
            ValidAudience = "EduScroll.TestAudience",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };

        // Valid token succeeds
        handler.ValidateToken(activeToken, baseParams, out _);

        // Expired token rejected
        Assert.Throws<SecurityTokenExpiredException>(() => handler.ValidateToken(expiredToken, baseParams, out _));

        // Tampered token rejected
        var parts = activeToken.Split('.');
        var tamperedSig = parts[2][..^2] + (parts[2][^2..] == "AA" ? "BB" : "AA");
        var tamperedToken = $"{parts[0]}.{parts[1]}.{tamperedSig}";
        Assert.ThrowsAny<SecurityTokenException>(() => handler.ValidateToken(tamperedToken, baseParams, out _));

        // Wrong issuer rejected
        var wrongIssuerParams = baseParams.Clone();
        wrongIssuerParams.ValidIssuer = "OtherIssuer";
        Assert.Throws<SecurityTokenInvalidIssuerException>(
            () => handler.ValidateToken(activeToken, wrongIssuerParams, out _));

        // Wrong audience rejected
        var wrongAudienceParams = baseParams.Clone();
        wrongAudienceParams.ValidAudience = "OtherAudience";
        Assert.Throws<SecurityTokenInvalidAudienceException>(
            () => handler.ValidateToken(activeToken, wrongAudienceParams, out _));
    }

    [Fact]
    public void GenerateRefreshTokenAndHash_ShouldBeCryptographicallyRandomAndDeterministicSha256()
    {
        var sut = new JwtTokenService(
            Options.Create(new JwtOptions
            {
                Issuer = "EduScroll.TestIssuer",
                Audience = "EduScroll.TestAudience",
                SecretKey = TestSigningKey
            }),
            TimeProvider.System);

        var tokens = Enumerable.Range(0, 20).Select(_ => sut.GenerateRefreshToken()).ToArray();
        Assert.Equal(tokens.Length, tokens.Distinct(StringComparer.Ordinal).Count());
        Assert.All(tokens, t => Assert.True(t.Length >= 80));

        var hash1 = sut.HashRefreshToken(tokens[0]);
        var hash2 = sut.HashRefreshToken(tokens[0]);
        var otherHash = sut.HashRefreshToken(tokens[1]);

        Assert.Equal(64, hash1.Length);
        Assert.Equal(hash1, hash2);
        Assert.NotEqual(tokens[0], hash1);
        Assert.NotEqual(hash1, otherHash);
    }

    [Theory]
    [InlineData("", "aud", TestSigningKey, false)]
    [InlineData("iss", "", TestSigningKey, false)]
    [InlineData("iss", "aud", "", false)]
    [InlineData("iss", "aud", "too-short-secret", false)]
    [InlineData("iss", "aud", "REPLACE_WITH_AT_LEAST_32_BYTE_SECRET_KEY", false)]
    [InlineData("iss", "aud", TestSigningKey, true)]
    public void JwtOptionsValidator_ShouldValidateRequiredFieldsAndKeyLength(
        string issuer,
        string audience,
        string secretKey,
        bool expectedSuccess)
    {
        var validator = new JwtOptionsValidator();
        var options = new JwtOptions
        {
            Issuer = issuer,
            Audience = audience,
            SecretKey = secretKey,
            AccessTokenExpirationMinutes = 15,
            RefreshTokenExpirationDays = 7,
            ClockSkewSeconds = 30
        };

        var result = validator.Validate(null, options);
        Assert.Equal(expectedSuccess, result.Succeeded);
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("Server=YOUR_SQL_SERVER;Database=EduScrollDb;", false)]
    [InlineData("not-a-valid-connection-string", false)]
    [InlineData("Database=EduScroll;Trusted_Connection=True;", false)]
    [InlineData("Server=localhost;Database=EduScroll;Trusted_Connection=True;", true)]
    public void DatabaseOptionsValidator_ShouldRejectMissingOrPlaceholderConnectionStrings(
        string connectionString,
        bool expectedSuccess)
    {
        var validator = new DatabaseOptionsValidator();
        var result = validator.Validate(null, new DatabaseOptions { DefaultConnection = connectionString });

        Assert.Equal(expectedSuccess, result.Succeeded);
    }

    private sealed class FixedTimeProvider(DateTimeOffset fixedUtcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => fixedUtcNow;
    }
}
