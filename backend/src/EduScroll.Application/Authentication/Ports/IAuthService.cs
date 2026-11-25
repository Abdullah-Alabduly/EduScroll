using EduScroll.Application.Authentication.Contracts;

namespace EduScroll.Application.Authentication.Ports;

public interface IAuthService
{
    Task<UserProfileDto> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);

    Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken);

    Task<AuthResponse> RefreshAsync(RefreshTokenRequest request, CancellationToken cancellationToken);

    Task<MessageResponse> LogoutAsync(Guid currentUserId, LogoutRequest request, CancellationToken cancellationToken);

    Task<UserProfileDto> GetCurrentUserAsync(Guid currentUserId, CancellationToken cancellationToken);
}
