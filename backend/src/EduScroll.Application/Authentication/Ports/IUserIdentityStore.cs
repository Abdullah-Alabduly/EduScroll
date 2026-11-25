using EduScroll.Application.Authentication.Models;

namespace EduScroll.Application.Authentication.Ports;

public interface IUserIdentityStore
{
    Task<IdentityCreateUserResult> CreateUserAsync(
        string fullName,
        string userName,
        string email,
        string password,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken);

    Task<IdentityCredentialCheckResult> CheckCredentialsAsync(
        string email,
        string password,
        CancellationToken cancellationToken);

    Task<UserLookupResult> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken);
}
