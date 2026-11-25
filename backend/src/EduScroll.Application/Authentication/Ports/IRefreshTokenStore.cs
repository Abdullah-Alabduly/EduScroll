using EduScroll.Domain.Entities;

namespace EduScroll.Application.Authentication.Ports;

public interface IRefreshTokenStore
{
    Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken);

    Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task<bool> TryRotateAsync(
        RefreshToken currentToken,
        RefreshToken replacementToken,
        CancellationToken cancellationToken);

    Task<bool> TryRevokeAsync(RefreshToken token, CancellationToken cancellationToken);
}
