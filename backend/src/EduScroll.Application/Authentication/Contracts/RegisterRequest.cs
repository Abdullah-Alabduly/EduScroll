namespace EduScroll.Application.Authentication.Contracts;

public sealed record RegisterRequest(
    string FullName,
    string UserName,
    string Email,
    string Password);
