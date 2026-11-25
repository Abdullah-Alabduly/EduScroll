using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using EduScroll.Application.Authentication.Contracts;
using EduScroll.Application.Authentication.Models;
using EduScroll.Application.Authentication.Ports;
using EduScroll.Infrastructure.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace EduScroll.Infrastructure.Authentication;

public sealed class JwtTokenService(
    IOptions<JwtOptions> jwtOptions,
    TimeProvider timeProvider) : ITokenService
{
    public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(jwtOptions.Value.RefreshTokenExpirationDays);

    public AccessTokenResult GenerateAccessToken(UserProfileDto user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var options = jwtOptions.Value;
        var keyBytes = Encoding.UTF8.GetBytes(options.SecretKey);
        if (keyBytes.Length < JwtOptions.MinimumSecretKeyByteLength)
        {
            throw new InvalidOperationException(
                $"Jwt:SecretKey must be at least {JwtOptions.MinimumSecretKeyByteLength} bytes.");
        }

        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(options.AccessTokenExpirationMinutes);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString("D")),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new Claim(
                JwtRegisteredClaimNames.Iat,
                now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture),
                ClaimValueTypes.Integer64),
            new Claim(JwtRegisteredClaimNames.Email, user.Email),
            new Claim(JwtRegisteredClaimNames.Name, user.UserName)
        };

        var signingKey = new SymmetricSecurityKey(keyBytes);
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: options.Issuer,
            audience: options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var tokenHandler = new JwtSecurityTokenHandler { MapInboundClaims = false };
        var serialized = tokenHandler.WriteToken(token);
        return new AccessTokenResult(serialized, expiresAt);
    }

    public string GenerateRefreshToken()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(64);
        return Base64UrlEncoder.Encode(randomBytes);
    }

    public string HashRefreshToken(string rawRefreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawRefreshToken);

        var tokenBytes = Encoding.UTF8.GetBytes(rawRefreshToken);
        var hashBytes = SHA256.HashData(tokenBytes);
        return Convert.ToHexStringLower(hashBytes);
    }
}
