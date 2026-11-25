namespace EduScroll.Application.Authentication.Contracts;

public sealed record LoginRequest(
    string Email,
    string Password);
