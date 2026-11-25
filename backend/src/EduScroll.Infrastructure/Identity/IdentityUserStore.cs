using EduScroll.Application.Authentication.Contracts;
using EduScroll.Application.Authentication.Models;
using EduScroll.Application.Authentication.Ports;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EduScroll.Infrastructure.Identity;

public sealed class IdentityUserStore(UserManager<ApplicationUser> userManager) : IUserIdentityStore
{
    private static readonly ApplicationUser TimingMitigationDummyUser = new();
    private static readonly Lazy<string> TimingMitigationDummyHash = new(() =>
        new PasswordHasher<ApplicationUser>().HashPassword(
            TimingMitigationDummyUser,
            "TimingMitigationDummyPassword_NotReal!123"));
    public async Task<IdentityCreateUserResult> CreateUserAsync(
        string fullName,
        string userName,
        string email,
        string password,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (await userManager.FindByEmailAsync(email) is not null)
        {
            return IdentityCreateUserResult.DuplicateEmail();
        }

        if (await userManager.FindByNameAsync(userName) is not null)
        {
            return IdentityCreateUserResult.DuplicateUserName();
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            FullName = fullName,
            UserName = userName,
            Email = email,
            CreatedAt = createdAtUtc,
            UpdatedAt = createdAtUtc,
            IsActive = true
        };

        IdentityResult createResult;
        try
        {
            createResult = await userManager.CreateAsync(user, password);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex, out var sqlErrorMessage))
        {
            if (sqlErrorMessage.Contains("EmailIndex", StringComparison.OrdinalIgnoreCase) ||
                await userManager.FindByEmailAsync(email) is not null)
            {
                return IdentityCreateUserResult.DuplicateEmail();
            }

            if (sqlErrorMessage.Contains("UserNameIndex", StringComparison.OrdinalIgnoreCase) ||
                await userManager.FindByNameAsync(userName) is not null)
            {
                return IdentityCreateUserResult.DuplicateUserName();
            }

            throw;
        }

        if (!createResult.Succeeded)
        {
            if (createResult.Errors.Any(e =>
                    string.Equals(e.Code, nameof(IdentityErrorDescriber.DuplicateEmail), StringComparison.Ordinal)))
            {
                return IdentityCreateUserResult.DuplicateEmail();
            }

            if (createResult.Errors.Any(e =>
                    string.Equals(e.Code, nameof(IdentityErrorDescriber.DuplicateUserName), StringComparison.Ordinal)))
            {
                return IdentityCreateUserResult.DuplicateUserName();
            }

            return IdentityCreateUserResult.Failed(MapIdentityErrors(createResult.Errors));
        }

        return IdentityCreateUserResult.Success(MapToDto(user));
    }

    public async Task<IdentityCredentialCheckResult> CheckCredentialsAsync(
        string email,
        string password,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByEmailAsync(email);
        if (user is null)
        {
            _ = userManager.PasswordHasher.VerifyHashedPassword(
                TimingMitigationDummyUser,
                TimingMitigationDummyHash.Value,
                password);
            return new IdentityCredentialCheckResult(CredentialValidationStatus.InvalidCredentials, null);
        }

        if (await userManager.IsLockedOutAsync(user))
        {
            return new IdentityCredentialCheckResult(CredentialValidationStatus.LockedOut, null);
        }

        var isPasswordValid = await userManager.CheckPasswordAsync(user, password);
        if (!isPasswordValid)
        {
            await userManager.AccessFailedAsync(user);
            if (await userManager.IsLockedOutAsync(user))
            {
                return new IdentityCredentialCheckResult(CredentialValidationStatus.LockedOut, null);
            }

            return new IdentityCredentialCheckResult(CredentialValidationStatus.InvalidCredentials, null);
        }

        if (user.AccessFailedCount > 0)
        {
            await userManager.ResetAccessFailedCountAsync(user);
        }

        if (!user.IsActive)
        {
            return new IdentityCredentialCheckResult(CredentialValidationStatus.Inactive, MapToDto(user));
        }

        return new IdentityCredentialCheckResult(CredentialValidationStatus.Authenticated, MapToDto(user));
    }

    public async Task<UserLookupResult> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return new UserLookupResult(UserLookupStatus.NotFound, null);
        }

        if (!user.IsActive)
        {
            return new UserLookupResult(UserLookupStatus.Inactive, MapToDto(user));
        }

        return new UserLookupResult(UserLookupStatus.Found, MapToDto(user));
    }

    private static UserProfileDto MapToDto(ApplicationUser user) =>
        new(
            user.Id,
            user.FullName,
            user.UserName ?? string.Empty,
            user.Email ?? string.Empty,
            user.CreatedAt,
            user.UpdatedAt,
            user.IsActive);

    private static Dictionary<string, string[]> MapIdentityErrors(IEnumerable<IdentityError> errors)
    {
        var grouped = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var error in errors)
        {
            var key = error.Code switch
            {
                var c when c.StartsWith("Password", StringComparison.Ordinal) => "password",
                var c when c.Contains("Email", StringComparison.Ordinal) => "email",
                var c when c.Contains("UserName", StringComparison.Ordinal) => "userName",
                _ => "general"
            };

            if (!grouped.TryGetValue(key, out var list))
            {
                list = [];
                grouped[key] = list;
            }

            list.Add(error.Description);
        }

        return grouped.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToArray(), StringComparer.Ordinal);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex, out string message)
    {
        for (Exception? current = ex.InnerException; current is not null; current = current.InnerException)
        {
            if (current is SqlException sqlEx && (sqlEx.Number == 2601 || sqlEx.Number == 2627))
            {
                message = sqlEx.Message;
                return true;
            }
        }

        message = string.Empty;
        return false;
    }
}
