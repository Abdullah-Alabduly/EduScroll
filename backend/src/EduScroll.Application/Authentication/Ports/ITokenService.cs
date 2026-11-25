using EduScroll.Application.Authentication.Contracts;
using EduScroll.Application.Authentication.Models;

namespace EduScroll.Application.Authentication.Ports;

public interface ITokenService
{
    TimeSpan RefreshTokenLifetime { get; }

    AccessTokenResult GenerateAccessToken(UserProfileDto user);

    string GenerateRefreshToken();

    string HashRefreshToken(string rawRefreshToken);
}
