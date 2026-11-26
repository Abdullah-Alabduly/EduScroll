using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using EduScroll.Application.Exceptions;

namespace EduScroll.API.Security;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetRequiredUserId(this ClaimsPrincipal user)
    {
        ArgumentNullException.ThrowIfNull(user);

        var rawUserId = user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? user.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!Guid.TryParse(rawUserId, out var userId) || userId == Guid.Empty)
        {
            throw new UnauthorizedAppException(
                "Authenticated user identifier is missing or invalid.",
                "UNAUTHORIZED");
        }

        return userId;
    }
}
