using EduScroll.Application.Authentication.Contracts;
using EduScroll.Application.Authentication.Models;
using EduScroll.Application.Authentication.Ports;
using EduScroll.Application.Exceptions;
using EduScroll.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EduScroll.Application.Authentication.Services;

public sealed class AuthService(
    IUserIdentityStore identityStore,
    IRefreshTokenStore refreshTokenStore,
    ITokenService tokenService,
    TimeProvider timeProvider,
    ILogger<AuthService> logger) : IAuthService
{
    public async Task<UserProfileDto> RegisterAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var (fullName, userName, email, password) = AuthRequestValidator.ValidateRegister(request);
        var now = timeProvider.GetUtcNow();

        var result = await identityStore.CreateUserAsync(
            fullName,
            userName,
            email,
            password,
            now,
            cancellationToken);

        if (!result.Succeeded)
        {
            if (result.IsDuplicateEmail)
            {
                throw new ConflictAppException(
                    "An account with this email already exists.",
                    "DUPLICATE_EMAIL");
            }

            if (result.IsDuplicateUserName)
            {
                throw new ConflictAppException(
                    "This username is already taken.",
                    "DUPLICATE_USERNAME");
            }

            throw new ValidationAppException(
                "Registration failed validation.",
                result.ValidationErrors);
        }

        var createdUser = result.User ?? throw new InvalidOperationException("Created user profile was missing.");
        logger.LogInformation("User account {UserId} registered.", createdUser.Id);
        return createdUser;
    }

    public async Task<AuthResponse> LoginAsync(
        LoginRequest request,
        CancellationToken cancellationToken)
    {
        var (email, password) = AuthRequestValidator.ValidateLogin(request);

        var checkResult = await identityStore.CheckCredentialsAsync(email, password, cancellationToken);
        switch (checkResult.Status)
        {
            case CredentialValidationStatus.InvalidCredentials:
                logger.LogWarning("Authentication rejected due to invalid credentials.");
                throw new UnauthorizedAppException("Invalid email or password.", "INVALID_CREDENTIALS");

            case CredentialValidationStatus.LockedOut:
                logger.LogWarning("Authentication rejected because account is locked out.");
                throw new ForbiddenAppException(
                    "Account is temporarily locked due to multiple failed login attempts.",
                    "ACCOUNT_LOCKED");

            case CredentialValidationStatus.Inactive:
                logger.LogWarning("Authentication rejected for inactive user {UserId}.", checkResult.User?.Id);
                throw new ForbiddenAppException("This account is inactive.", "ACCOUNT_INACTIVE");

            case CredentialValidationStatus.Authenticated:
                break;

            default:
                throw new InvalidOperationException($"Unsupported credential validation status: {checkResult.Status}");
        }

        var user = checkResult.User ?? throw new InvalidOperationException("Authenticated user profile was missing.");
        var now = timeProvider.GetUtcNow();

        var accessToken = tokenService.GenerateAccessToken(user);
        var rawRefreshToken = tokenService.GenerateRefreshToken();
        var refreshTokenHash = tokenService.HashRefreshToken(rawRefreshToken);
        var refreshExpiresAt = now.Add(tokenService.RefreshTokenLifetime);

        var refreshTokenEntity = RefreshToken.Create(user.Id, refreshTokenHash, now, refreshExpiresAt);
        await refreshTokenStore.AddAsync(refreshTokenEntity, cancellationToken);

        logger.LogInformation("User {UserId} authenticated successfully.", user.Id);

        return new AuthResponse(
            accessToken.Token,
            "Bearer",
            accessToken.ExpiresAtUtc,
            rawRefreshToken,
            refreshExpiresAt,
            user);
    }

    public async Task<AuthResponse> RefreshAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var rawToken = AuthRequestValidator.ValidateRefreshTokenValue(request.RefreshToken);
        var now = timeProvider.GetUtcNow();
        var tokenHash = tokenService.HashRefreshToken(rawToken);

        var existingToken = await refreshTokenStore.GetByTokenHashAsync(tokenHash, cancellationToken);
        if (existingToken is null)
        {
            logger.LogWarning("Refresh attempt rejected: unknown refresh token.");
            throw new UnauthorizedAppException("Refresh token is invalid.", "INVALID_REFRESH_TOKEN");
        }

        if (existingToken.IsRevoked)
        {
            logger.LogWarning(
                "Refresh attempt rejected: revoked refresh token reuse detected for user {UserId}.",
                existingToken.UserId);
            throw new UnauthorizedAppException("Refresh token has been revoked.", "REFRESH_TOKEN_REVOKED");
        }

        if (existingToken.IsExpired(now))
        {
            logger.LogWarning("Refresh attempt rejected: expired refresh token for user {UserId}.", existingToken.UserId);
            throw new UnauthorizedAppException("Refresh token has expired.", "REFRESH_TOKEN_EXPIRED");
        }

        var userLookup = await identityStore.GetUserByIdAsync(existingToken.UserId, cancellationToken);
        switch (userLookup.Status)
        {
            case UserLookupStatus.NotFound:
                throw new UnauthorizedAppException("User account no longer exists.", "INVALID_REFRESH_TOKEN");

            case UserLookupStatus.Inactive:
                throw new ForbiddenAppException("This account is inactive.", "ACCOUNT_INACTIVE");

            case UserLookupStatus.Found:
                break;

            default:
                throw new InvalidOperationException($"Unsupported user lookup status: {userLookup.Status}");
        }

        var user = userLookup.User ?? throw new InvalidOperationException("User profile was missing.");

        var newRawRefreshToken = tokenService.GenerateRefreshToken();
        var newRefreshTokenHash = tokenService.HashRefreshToken(newRawRefreshToken);
        var newRefreshExpiresAt = now.Add(tokenService.RefreshTokenLifetime);

        existingToken.Revoke(now, "Rotated", newRefreshTokenHash);
        var replacementToken = RefreshToken.Create(user.Id, newRefreshTokenHash, now, newRefreshExpiresAt);

        var rotated = await refreshTokenStore.TryRotateAsync(existingToken, replacementToken, cancellationToken);
        if (!rotated)
        {
            logger.LogWarning(
                "Refresh attempt rejected due to concurrent token rotation for user {UserId}.",
                existingToken.UserId);
            throw new UnauthorizedAppException(
                "Refresh token has already been used.",
                "REFRESH_TOKEN_REUSED");
        }

        var accessToken = tokenService.GenerateAccessToken(user);
        logger.LogInformation("Refresh token rotated for user {UserId}.", user.Id);

        return new AuthResponse(
            accessToken.Token,
            "Bearer",
            accessToken.ExpiresAtUtc,
            newRawRefreshToken,
            newRefreshExpiresAt,
            user);
    }

    public async Task<MessageResponse> LogoutAsync(
        Guid currentUserId,
        LogoutRequest request,
        CancellationToken cancellationToken)
    {
        if (currentUserId == Guid.Empty)
        {
            throw new UnauthorizedAppException("Authentication is required.", "UNAUTHORIZED");
        }

        ArgumentNullException.ThrowIfNull(request);
        var rawToken = AuthRequestValidator.ValidateRefreshTokenValue(request.RefreshToken);
        var tokenHash = tokenService.HashRefreshToken(rawToken);

        var existingToken = await refreshTokenStore.GetByTokenHashAsync(tokenHash, cancellationToken);
        if (existingToken is null || existingToken.UserId != currentUserId)
        {
            logger.LogWarning("Logout rejected: refresh token invalid for user {UserId}.", currentUserId);
            throw new UnauthorizedAppException("Refresh token is invalid.", "INVALID_REFRESH_TOKEN");
        }

        if (!existingToken.IsRevoked)
        {
            existingToken.Revoke(timeProvider.GetUtcNow(), "UserLogout");
            await refreshTokenStore.TryRevokeAsync(existingToken, cancellationToken);
        }

        logger.LogInformation("User {UserId} signed out and revoked refresh token.", currentUserId);
        return new MessageResponse("Signed out successfully.");
    }

    public async Task<UserProfileDto> GetCurrentUserAsync(
        Guid currentUserId,
        CancellationToken cancellationToken)
    {
        if (currentUserId == Guid.Empty)
        {
            throw new UnauthorizedAppException("Authentication is required.", "UNAUTHORIZED");
        }

        var lookup = await identityStore.GetUserByIdAsync(currentUserId, cancellationToken);
        return lookup.Status switch
        {
            UserLookupStatus.Found => lookup.User ?? throw new InvalidOperationException("User profile was missing."),
            UserLookupStatus.Inactive => throw new ForbiddenAppException("This account is inactive.", "ACCOUNT_INACTIVE"),
            _ => throw new UnauthorizedAppException("Authenticated user account was not found.", "USER_NOT_FOUND")
        };
    }
}
