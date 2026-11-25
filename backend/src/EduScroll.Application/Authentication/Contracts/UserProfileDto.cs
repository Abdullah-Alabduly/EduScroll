namespace EduScroll.Application.Authentication.Contracts;

public sealed record UserProfileDto(
    Guid Id,
    string FullName,
    string UserName,
    string Email,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool IsActive);
