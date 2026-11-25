using EduScroll.Domain.Entities;
using EduScroll.Domain.Exceptions;

namespace EduScroll.Domain.Tests;

public sealed class RefreshTokenTests
{
    [Fact]
    public void Create_ShouldInitializeProperties_WhenArgumentsAreValid()
    {
        var userId = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        var expiresAt = createdAt.AddDays(7);

        var token = RefreshToken.Create(userId, "abc123hash", createdAt, expiresAt);

        Assert.NotEqual(Guid.Empty, token.Id);
        Assert.Equal(userId, token.UserId);
        Assert.Equal("abc123hash", token.TokenHash);
        Assert.Equal(createdAt, token.CreatedAt);
        Assert.Equal(expiresAt, token.ExpiresAt);
        Assert.Null(token.RevokedAt);
        Assert.Null(token.ReplacedByTokenHash);
        Assert.Null(token.RevocationReason);
        Assert.False(token.IsRevoked);
        Assert.True(token.IsActive(createdAt));
    }

    [Fact]
    public void Create_ShouldThrowDomainException_WhenUserIdIsEmpty()
    {
        var now = DateTimeOffset.UtcNow;

        var ex = Assert.Throws<InvalidRefreshTokenDomainException>(
            () => RefreshToken.Create(Guid.Empty, "hash", now, now.AddDays(1)));

        Assert.Equal("REFRESH_TOKEN_INVALID_USER", ex.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_ShouldThrowDomainException_WhenTokenHashIsInvalid(string invalidHash)
    {
        var now = DateTimeOffset.UtcNow;

        var ex = Assert.Throws<InvalidRefreshTokenDomainException>(
            () => RefreshToken.Create(Guid.NewGuid(), invalidHash, now, now.AddDays(1)));

        Assert.Equal("REFRESH_TOKEN_INVALID_HASH", ex.ErrorCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_ShouldThrowDomainException_WhenExpirationIsNotLaterThanCreation(int minuteOffset)
    {
        var now = DateTimeOffset.UtcNow;

        var ex = Assert.Throws<InvalidRefreshTokenDomainException>(
            () => RefreshToken.Create(Guid.NewGuid(), "hash", now, now.AddMinutes(minuteOffset)));

        Assert.Equal("REFRESH_TOKEN_INVALID_EXPIRY", ex.ErrorCode);
    }

    [Fact]
    public void IsExpiredAndIsActive_ShouldEvaluateBoundaryCorrectly()
    {
        var createdAt = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        var expiresAt = createdAt.AddMinutes(30);
        var token = RefreshToken.Create(Guid.NewGuid(), "hash", createdAt, expiresAt);

        Assert.False(token.IsExpired(expiresAt.AddTicks(-1)));
        Assert.True(token.IsActive(expiresAt.AddTicks(-1)));

        Assert.True(token.IsExpired(expiresAt));
        Assert.False(token.IsActive(expiresAt));
    }

    [Fact]
    public void Revoke_ShouldMarkRevoked_AndPreventSubsequentRevocation()
    {
        var createdAt = new DateTimeOffset(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
        var token = RefreshToken.Create(Guid.NewGuid(), "hash1", createdAt, createdAt.AddDays(7));
        var revokedAt = createdAt.AddHours(1);

        token.Revoke(revokedAt, "Rotated", "hash2");

        Assert.True(token.IsRevoked);
        Assert.False(token.IsActive(revokedAt));
        Assert.Equal(revokedAt, token.RevokedAt);
        Assert.Equal("Rotated", token.RevocationReason);
        Assert.Equal("hash2", token.ReplacedByTokenHash);

        var ex = Assert.Throws<InvalidRefreshTokenDomainException>(
            () => token.Revoke(revokedAt.AddMinutes(1), "UserLogout"));

        Assert.Equal("REFRESH_TOKEN_ALREADY_REVOKED", ex.ErrorCode);
    }

    [Fact]
    public void Revoke_ShouldThrowDomainException_WhenReplacementHashIsWhitespace()
    {
        var createdAt = DateTimeOffset.UtcNow;
        var token = RefreshToken.Create(Guid.NewGuid(), "hash1", createdAt, createdAt.AddDays(1));

        var ex = Assert.Throws<InvalidRefreshTokenDomainException>(
            () => token.Revoke(createdAt.AddMinutes(5), "Rotated", "   "));

        Assert.Equal("REFRESH_TOKEN_INVALID_REPLACEMENT", ex.ErrorCode);
    }
}
