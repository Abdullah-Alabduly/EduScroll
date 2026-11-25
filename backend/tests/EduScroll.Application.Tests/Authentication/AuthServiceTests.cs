using System.Security.Cryptography;
using System.Text;
using EduScroll.Application.Authentication.Contracts;
using EduScroll.Application.Authentication.Models;
using EduScroll.Application.Authentication.Ports;
using EduScroll.Application.Authentication.Services;
using EduScroll.Application.Exceptions;
using EduScroll.Domain.Entities;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduScroll.Application.Tests.Authentication;

public sealed class AuthServiceTests
{
    private readonly FakeTimeProvider _timeProvider = new(new DateTimeOffset(2026, 11, 17, 10, 0, 0, TimeSpan.Zero));
    private readonly InMemoryUserIdentityStore _identityStore = new();
    private readonly InMemoryRefreshTokenStore _refreshTokenStore = new();
    private readonly StubTokenService _tokenService = new();
    private readonly AuthService _sut;

    public AuthServiceTests()
    {
        _sut = new AuthService(
            _identityStore,
            _refreshTokenStore,
            _tokenService,
            _timeProvider,
            NullLogger<AuthService>.Instance);
    }

    [Fact]
    public async Task RegisterAsync_ShouldCreateUser_AndReturnProfile()
    {
        var profile = await _sut.RegisterAsync(
            new RegisterRequest("Grace Hopper", "grace.hopper", "grace@example.com", "StrongPass123!"),
            CancellationToken.None);

        Assert.NotEqual(Guid.Empty, profile.Id);
        Assert.Equal("Grace Hopper", profile.FullName);
        Assert.Equal("grace.hopper", profile.UserName);
        Assert.Equal("grace@example.com", profile.Email);
        Assert.True(profile.IsActive);
        Assert.Equal(_timeProvider.GetUtcNow(), profile.CreatedAt);
    }

    [Fact]
    public async Task RegisterAsync_ShouldThrowConflict_WhenEmailAlreadyExists()
    {
        await _sut.RegisterAsync(
            new RegisterRequest("Grace Hopper", "grace1", "grace@example.com", "StrongPass123!"),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() => _sut.RegisterAsync(
            new RegisterRequest("Grace Two", "grace2", "GRACE@example.com", "StrongPass123!"),
            CancellationToken.None));

        Assert.Equal("DUPLICATE_EMAIL", ex.ErrorCode);
    }

    [Fact]
    public async Task RegisterAsync_ShouldThrowConflict_WhenUserNameAlreadyExists()
    {
        await _sut.RegisterAsync(
            new RegisterRequest("Grace Hopper", "grace.hopper", "grace1@example.com", "StrongPass123!"),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<ConflictAppException>(() => _sut.RegisterAsync(
            new RegisterRequest("Grace Two", "GRACE.HOPPER", "grace2@example.com", "StrongPass123!"),
            CancellationToken.None));

        Assert.Equal("DUPLICATE_USERNAME", ex.ErrorCode);
    }

    [Fact]
    public async Task LoginAsync_ShouldReturnTokens_AndStoreOnlyHashedRefreshToken()
    {
        var user = await _sut.RegisterAsync(
            new RegisterRequest("Ada Lovelace", "ada", "ada@example.com", "StrongPass123!"),
            CancellationToken.None);

        var auth = await _sut.LoginAsync(
            new LoginRequest("ada@example.com", "StrongPass123!"),
            CancellationToken.None);

        Assert.Equal("Bearer", auth.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));
        Assert.Equal(user.Id, auth.User.Id);

        var stored = Assert.Single(_refreshTokenStore.Tokens);
        Assert.NotEqual(auth.RefreshToken, stored.TokenHash);
        Assert.Equal(_tokenService.HashRefreshToken(auth.RefreshToken), stored.TokenHash);
        Assert.Equal(_timeProvider.GetUtcNow().AddDays(7), stored.ExpiresAt);
    }

    [Fact]
    public async Task LoginAsync_ShouldThrowUnauthorized_WhenCredentialsAreInvalid()
    {
        await _sut.RegisterAsync(
            new RegisterRequest("Ada Lovelace", "ada", "ada@example.com", "StrongPass123!"),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<UnauthorizedAppException>(() => _sut.LoginAsync(
            new LoginRequest("ada@example.com", "WrongPass999!"),
            CancellationToken.None));

        Assert.Equal("INVALID_CREDENTIALS", ex.ErrorCode);
    }

    [Fact]
    public async Task LoginAsync_ShouldThrowForbidden_WhenAccountIsInactive()
    {
        var user = await _sut.RegisterAsync(
            new RegisterRequest("Ada Lovelace", "ada", "ada@example.com", "StrongPass123!"),
            CancellationToken.None);
        _identityStore.SetActive(user.Id, false);

        var ex = await Assert.ThrowsAsync<ForbiddenAppException>(() => _sut.LoginAsync(
            new LoginRequest("ada@example.com", "StrongPass123!"),
            CancellationToken.None));

        Assert.Equal("ACCOUNT_INACTIVE", ex.ErrorCode);
    }

    [Fact]
    public async Task LoginAsync_ShouldThrowForbidden_WhenAccountIsLockedOut()
    {
        var user = await _sut.RegisterAsync(
            new RegisterRequest("Ada Lovelace", "ada", "ada@example.com", "StrongPass123!"),
            CancellationToken.None);
        _identityStore.SetLockedOut(user.Id, true);

        var ex = await Assert.ThrowsAsync<ForbiddenAppException>(() => _sut.LoginAsync(
            new LoginRequest("ada@example.com", "StrongPass123!"),
            CancellationToken.None));

        Assert.Equal("ACCOUNT_LOCKED", ex.ErrorCode);
    }

    [Fact]
    public async Task RefreshAsync_ShouldRotateToken_RevokePrevious_AndRejectReuse()
    {
        await _sut.RegisterAsync(
            new RegisterRequest("Ada Lovelace", "ada", "ada@example.com", "StrongPass123!"),
            CancellationToken.None);
        var login = await _sut.LoginAsync(
            new LoginRequest("ada@example.com", "StrongPass123!"),
            CancellationToken.None);

        _timeProvider.Advance(TimeSpan.FromMinutes(20));

        var refreshed = await _sut.RefreshAsync(
            new RefreshTokenRequest(login.RefreshToken),
            CancellationToken.None);

        Assert.NotEqual(login.RefreshToken, refreshed.RefreshToken);
        Assert.Equal(2, _refreshTokenStore.Tokens.Count);

        var oldStored = _refreshTokenStore.Tokens.Single(
            t => t.TokenHash == _tokenService.HashRefreshToken(login.RefreshToken));
        Assert.True(oldStored.IsRevoked);
        Assert.Equal(_tokenService.HashRefreshToken(refreshed.RefreshToken), oldStored.ReplacedByTokenHash);

        var reuseEx = await Assert.ThrowsAsync<UnauthorizedAppException>(
            () => _sut.RefreshAsync(new RefreshTokenRequest(login.RefreshToken), CancellationToken.None));
        Assert.Equal("REFRESH_TOKEN_REVOKED", reuseEx.ErrorCode);
    }

    [Fact]
    public async Task RefreshAsync_ShouldThrowUnauthorized_WhenTokenIsExpired()
    {
        await _sut.RegisterAsync(
            new RegisterRequest("Ada Lovelace", "ada", "ada@example.com", "StrongPass123!"),
            CancellationToken.None);
        var login = await _sut.LoginAsync(
            new LoginRequest("ada@example.com", "StrongPass123!"),
            CancellationToken.None);

        _timeProvider.Advance(TimeSpan.FromDays(8));

        var ex = await Assert.ThrowsAsync<UnauthorizedAppException>(
            () => _sut.RefreshAsync(new RefreshTokenRequest(login.RefreshToken), CancellationToken.None));

        Assert.Equal("REFRESH_TOKEN_EXPIRED", ex.ErrorCode);
    }

    [Fact]
    public async Task RefreshAsync_ShouldThrowUnauthorized_WhenConcurrentRotationFails()
    {
        await _sut.RegisterAsync(
            new RegisterRequest("Ada Lovelace", "ada", "ada@example.com", "StrongPass123!"),
            CancellationToken.None);
        var login = await _sut.LoginAsync(
            new LoginRequest("ada@example.com", "StrongPass123!"),
            CancellationToken.None);

        _refreshTokenStore.SimulateNextRotationConflict = true;

        var ex = await Assert.ThrowsAsync<UnauthorizedAppException>(
            () => _sut.RefreshAsync(new RefreshTokenRequest(login.RefreshToken), CancellationToken.None));

        Assert.Equal("REFRESH_TOKEN_REUSED", ex.ErrorCode);
    }

    [Fact]
    public async Task LogoutAsync_ShouldRevokeOwnedToken_AndPreventSubsequentRefresh()
    {
        var user = await _sut.RegisterAsync(
            new RegisterRequest("Ada Lovelace", "ada", "ada@example.com", "StrongPass123!"),
            CancellationToken.None);
        var login = await _sut.LoginAsync(
            new LoginRequest("ada@example.com", "StrongPass123!"),
            CancellationToken.None);

        var response = await _sut.LogoutAsync(
            user.Id,
            new LogoutRequest(login.RefreshToken),
            CancellationToken.None);

        Assert.Equal("Signed out successfully.", response.Message);

        var refreshEx = await Assert.ThrowsAsync<UnauthorizedAppException>(
            () => _sut.RefreshAsync(new RefreshTokenRequest(login.RefreshToken), CancellationToken.None));
        Assert.Equal("REFRESH_TOKEN_REVOKED", refreshEx.ErrorCode);
    }

    [Fact]
    public async Task LogoutAsync_ShouldRejectTokenOwnedByDifferentUser()
    {
        await _sut.RegisterAsync(
            new RegisterRequest("Ada Lovelace", "ada", "ada@example.com", "StrongPass123!"),
            CancellationToken.None);
        var otherUser = await _sut.RegisterAsync(
            new RegisterRequest("Alan Turing", "alan", "alan@example.com", "StrongPass123!"),
            CancellationToken.None);
        var adaLogin = await _sut.LoginAsync(
            new LoginRequest("ada@example.com", "StrongPass123!"),
            CancellationToken.None);

        var ex = await Assert.ThrowsAsync<UnauthorizedAppException>(() => _sut.LogoutAsync(
            otherUser.Id,
            new LogoutRequest(adaLogin.RefreshToken),
            CancellationToken.None));

        Assert.Equal("INVALID_REFRESH_TOKEN", ex.ErrorCode);
    }

    [Fact]
    public async Task GetCurrentUserAsync_ShouldReturnProfile_AndRejectInactiveOrMissingUser()
    {
        var user = await _sut.RegisterAsync(
            new RegisterRequest("Ada Lovelace", "ada", "ada@example.com", "StrongPass123!"),
            CancellationToken.None);

        var profile = await _sut.GetCurrentUserAsync(user.Id, CancellationToken.None);
        Assert.Equal(user.Email, profile.Email);

        _identityStore.SetActive(user.Id, false);
        var inactiveEx = await Assert.ThrowsAsync<ForbiddenAppException>(
            () => _sut.GetCurrentUserAsync(user.Id, CancellationToken.None));
        Assert.Equal("ACCOUNT_INACTIVE", inactiveEx.ErrorCode);

        var missingEx = await Assert.ThrowsAsync<UnauthorizedAppException>(
            () => _sut.GetCurrentUserAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal("USER_NOT_FOUND", missingEx.ErrorCode);
    }

    private sealed class FakeTimeProvider(DateTimeOffset initialUtcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow = _utcNow.Add(delta);
    }

    private sealed class StubTokenService : ITokenService
    {
        private int _counter;

        public TimeSpan RefreshTokenLifetime => TimeSpan.FromDays(7);

        public AccessTokenResult GenerateAccessToken(UserProfileDto user) =>
            new($"jwt-token-{user.Id}-{++_counter}", DateTimeOffset.UtcNow.AddMinutes(15));

        public string GenerateRefreshToken() => $"raw-refresh-token-{++_counter}-{Guid.NewGuid():N}";

        public string HashRefreshToken(string rawRefreshToken)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawRefreshToken));
            return Convert.ToHexStringLower(bytes);
        }
    }

    private sealed class InMemoryUserIdentityStore : IUserIdentityStore
    {
        private sealed class StoredUser
        {
            public required UserProfileDto Profile { get; set; }
            public required string Password { get; init; }
            public bool LockedOut { get; set; }
        }

        private readonly Dictionary<Guid, StoredUser> _users = [];

        public void SetActive(Guid userId, bool isActive)
        {
            var user = _users[userId];
            user.Profile = user.Profile with { IsActive = isActive };
        }

        public void SetLockedOut(Guid userId, bool lockedOut) => _users[userId].LockedOut = lockedOut;

        public Task<IdentityCreateUserResult> CreateUserAsync(
            string fullName,
            string userName,
            string email,
            string password,
            DateTimeOffset createdAtUtc,
            CancellationToken cancellationToken)
        {
            if (_users.Values.Any(u => string.Equals(u.Profile.Email, email, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(IdentityCreateUserResult.DuplicateEmail());
            }

            if (_users.Values.Any(u => string.Equals(u.Profile.UserName, userName, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(IdentityCreateUserResult.DuplicateUserName());
            }

            var profile = new UserProfileDto(
                Guid.NewGuid(),
                fullName,
                userName,
                email,
                createdAtUtc,
                createdAtUtc,
                true);

            _users[profile.Id] = new StoredUser { Profile = profile, Password = password };
            return Task.FromResult(IdentityCreateUserResult.Success(profile));
        }

        public Task<IdentityCredentialCheckResult> CheckCredentialsAsync(
            string email,
            string password,
            CancellationToken cancellationToken)
        {
            var stored = _users.Values.FirstOrDefault(
                u => string.Equals(u.Profile.Email, email, StringComparison.OrdinalIgnoreCase));

            if (stored is null)
            {
                return Task.FromResult(new IdentityCredentialCheckResult(
                    CredentialValidationStatus.InvalidCredentials,
                    null));
            }

            if (stored.LockedOut)
            {
                return Task.FromResult(new IdentityCredentialCheckResult(
                    CredentialValidationStatus.LockedOut,
                    null));
            }

            if (!string.Equals(stored.Password, password, StringComparison.Ordinal))
            {
                return Task.FromResult(new IdentityCredentialCheckResult(
                    CredentialValidationStatus.InvalidCredentials,
                    null));
            }

            if (!stored.Profile.IsActive)
            {
                return Task.FromResult(new IdentityCredentialCheckResult(
                    CredentialValidationStatus.Inactive,
                    stored.Profile));
            }

            return Task.FromResult(new IdentityCredentialCheckResult(
                CredentialValidationStatus.Authenticated,
                stored.Profile));
        }

        public Task<UserLookupResult> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken)
        {
            if (!_users.TryGetValue(userId, out var stored))
            {
                return Task.FromResult(new UserLookupResult(UserLookupStatus.NotFound, null));
            }

            if (!stored.Profile.IsActive)
            {
                return Task.FromResult(new UserLookupResult(UserLookupStatus.Inactive, stored.Profile));
            }

            return Task.FromResult(new UserLookupResult(UserLookupStatus.Found, stored.Profile));
        }
    }

    private sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
    {
        public List<RefreshToken> Tokens { get; } = [];
        public bool SimulateNextRotationConflict { get; set; }

        public Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken)
        {
            Tokens.Add(refreshToken);
            return Task.CompletedTask;
        }

        public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(Tokens.FirstOrDefault(t => t.TokenHash == tokenHash));

        public Task<bool> TryRotateAsync(
            RefreshToken currentToken,
            RefreshToken replacementToken,
            CancellationToken cancellationToken)
        {
            if (SimulateNextRotationConflict)
            {
                SimulateNextRotationConflict = false;
                return Task.FromResult(false);
            }

            Tokens.Add(replacementToken);
            return Task.FromResult(true);
        }

        public Task<bool> TryRevokeAsync(RefreshToken token, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}
