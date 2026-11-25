using EduScroll.Application.Authentication.Contracts;

namespace EduScroll.Application.Authentication.Models;

public sealed record AccessTokenResult(
    string Token,
    DateTimeOffset ExpiresAtUtc);

public sealed record IdentityCreateUserResult(
    bool Succeeded,
    UserProfileDto? User,
    bool IsDuplicateEmail,
    bool IsDuplicateUserName,
    IReadOnlyDictionary<string, string[]> ValidationErrors)
{
    public static IdentityCreateUserResult Success(UserProfileDto user) =>
        new(true, user, false, false, new Dictionary<string, string[]>());

    public static IdentityCreateUserResult DuplicateEmail() =>
        new(false, null, true, false, new Dictionary<string, string[]>());

    public static IdentityCreateUserResult DuplicateUserName() =>
        new(false, null, false, true, new Dictionary<string, string[]>());

    public static IdentityCreateUserResult Failed(IReadOnlyDictionary<string, string[]> errors) =>
        new(false, null, false, false, errors);
}

public enum CredentialValidationStatus
{
    Authenticated = 1,
    InvalidCredentials = 2,
    LockedOut = 3,
    Inactive = 4
}

public sealed record IdentityCredentialCheckResult(
    CredentialValidationStatus Status,
    UserProfileDto? User);

public enum UserLookupStatus
{
    Found = 1,
    NotFound = 2,
    Inactive = 3
}

public sealed record UserLookupResult(
    UserLookupStatus Status,
    UserProfileDto? User);
