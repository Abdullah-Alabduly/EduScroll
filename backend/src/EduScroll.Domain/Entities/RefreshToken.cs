using EduScroll.Domain.Exceptions;

namespace EduScroll.Domain.Entities;

/// <summary>
/// Represents a persisted, revocable refresh token associated with a user account.
/// Only the cryptographic hash of the token is stored.
/// </summary>
public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public string? ReplacedByTokenHash { get; private set; }

    public string? RevocationReason { get; private set; }

    public bool IsRevoked => RevokedAt.HasValue;

    public bool IsExpired(DateTimeOffset utcNow) => utcNow >= ExpiresAt;

    public bool IsActive(DateTimeOffset utcNow) => !IsRevoked && !IsExpired(utcNow);

    public static RefreshToken Create(
        Guid userId,
        string tokenHash,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt)
    {
        if (userId == Guid.Empty)
        {
            throw new InvalidRefreshTokenDomainException(
                "REFRESH_TOKEN_INVALID_USER",
                "Refresh token must be associated with a valid user identifier.");
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new InvalidRefreshTokenDomainException(
                "REFRESH_TOKEN_INVALID_HASH",
                "Refresh token hash must not be empty.");
        }

        if (expiresAt <= createdAt)
        {
            throw new InvalidRefreshTokenDomainException(
                "REFRESH_TOKEN_INVALID_EXPIRY",
                "Refresh token expiration must be later than its creation timestamp.");
        }

        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash.Trim(),
            CreatedAt = createdAt,
            ExpiresAt = expiresAt
        };
    }

    public void Revoke(
        DateTimeOffset revokedAt,
        string? reason = null,
        string? replacedByTokenHash = null)
    {
        if (IsRevoked)
        {
            throw new InvalidRefreshTokenDomainException(
                "REFRESH_TOKEN_ALREADY_REVOKED",
                "Refresh token has already been revoked.");
        }

        if (replacedByTokenHash is not null && string.IsNullOrWhiteSpace(replacedByTokenHash))
        {
            throw new InvalidRefreshTokenDomainException(
                "REFRESH_TOKEN_INVALID_REPLACEMENT",
                "Replacement token hash cannot be whitespace.");
        }

        RevokedAt = revokedAt;
        RevocationReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        ReplacedByTokenHash = replacedByTokenHash?.Trim();
    }
}
