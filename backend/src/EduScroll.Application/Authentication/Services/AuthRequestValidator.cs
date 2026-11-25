using System.Net.Mail;
using System.Text.RegularExpressions;
using EduScroll.Application.Authentication.Contracts;
using EduScroll.Application.Exceptions;

namespace EduScroll.Application.Authentication.Services;

public static partial class AuthRequestValidator
{
    public const int MinFullNameLength = 2;
    public const int MaxFullNameLength = 200;
    public const int MinUserNameLength = 3;
    public const int MaxUserNameLength = 50;
    public const int MaxEmailLength = 256;
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 128;
    public const int MinUniquePasswordChars = 6;
    public const int MaxRefreshTokenLength = 512;

    public static (string FullName, string UserName, string Email, string Password) ValidateRegister(
        RegisterRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var fullName = (request.FullName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(fullName))
        {
            AddError(errors, "fullName", "Full name is required.");
        }
        else if (fullName.Length < MinFullNameLength || fullName.Length > MaxFullNameLength)
        {
            AddError(
                errors,
                "fullName",
                $"Full name must be between {MinFullNameLength} and {MaxFullNameLength} characters.");
        }

        var userName = (request.UserName ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(userName))
        {
            AddError(errors, "userName", "Username is required.");
        }
        else
        {
            if (userName.Length < MinUserNameLength || userName.Length > MaxUserNameLength)
            {
                AddError(
                    errors,
                    "userName",
                    $"Username must be between {MinUserNameLength} and {MaxUserNameLength} characters.");
            }

            if (!UserNameRegex().IsMatch(userName))
            {
                AddError(
                    errors,
                    "userName",
                    "Username may only contain letters, numbers, '.', '_', and '-'.");
            }
        }

        var email = (request.Email ?? string.Empty).Trim();
        ValidateEmail(email, errors);

        var password = request.Password ?? string.Empty;
        ValidateNewPassword(password, errors);

        ThrowIfHasErrors(errors);
        return (fullName, userName, email, password);
    }

    public static (string Email, string Password) ValidateLogin(LoginRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        var email = (request.Email ?? string.Empty).Trim();
        ValidateEmail(email, errors);

        var password = request.Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(password))
        {
            AddError(errors, "password", "Password is required.");
        }
        else if (password.Length > MaxPasswordLength)
        {
            AddError(errors, "password", $"Password must not exceed {MaxPasswordLength} characters.");
        }

        ThrowIfHasErrors(errors);
        return (email, password);
    }

    public static string ValidateRefreshTokenValue(string? token, string fieldName = "refreshToken")
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var trimmed = (token ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            AddError(errors, fieldName, "Refresh token is required.");
        }
        else if (trimmed.Length > MaxRefreshTokenLength)
        {
            AddError(errors, fieldName, $"Refresh token must not exceed {MaxRefreshTokenLength} characters.");
        }

        ThrowIfHasErrors(errors);
        return trimmed;
    }

    private static void ValidateEmail(string email, Dictionary<string, List<string>> errors)
    {
        if (string.IsNullOrEmpty(email))
        {
            AddError(errors, "email", "Email is required.");
            return;
        }

        if (email.Length > MaxEmailLength)
        {
            AddError(errors, "email", $"Email must not exceed {MaxEmailLength} characters.");
            return;
        }

        if (email.Any(char.IsWhiteSpace) ||
            email.Contains("..", StringComparison.Ordinal) ||
            !MailAddress.TryCreate(email, out var parsed) ||
            !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase) ||
            !IsValidEmailHost(parsed.Host))
        {
            AddError(errors, "email", "Email format is invalid.");
        }
    }

    private static bool IsValidEmailHost(string host)
    {
        if (string.IsNullOrWhiteSpace(host) || host.StartsWith('.') || host.EndsWith('.'))
        {
            return false;
        }

        var labels = host.Split('.');
        if (labels.Length < 2 || labels[^1].Length < 2)
        {
            return false;
        }

        foreach (var label in labels)
        {
            if (label.Length == 0 || label.StartsWith('-') || label.EndsWith('-'))
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateNewPassword(string password, Dictionary<string, List<string>> errors)
    {
        if (string.IsNullOrWhiteSpace(password))
        {
            AddError(errors, "password", "Password is required.");
            return;
        }

        if (password.Length < MinPasswordLength || password.Length > MaxPasswordLength)
        {
            AddError(
                errors,
                "password",
                $"Password must be between {MinPasswordLength} and {MaxPasswordLength} characters.");
        }

        if (!password.Any(char.IsUpper))
        {
            AddError(errors, "password", "Password must contain at least one uppercase letter.");
        }

        if (!password.Any(char.IsLower))
        {
            AddError(errors, "password", "Password must contain at least one lowercase letter.");
        }

        if (!password.Any(char.IsDigit))
        {
            AddError(errors, "password", "Password must contain at least one digit.");
        }

        if (!password.Any(ch => !char.IsLetterOrDigit(ch)))
        {
            AddError(errors, "password", "Password must contain at least one non-alphanumeric character.");
        }

        if (password.Distinct().Count() < MinUniquePasswordChars)
        {
            AddError(
                errors,
                "password",
                $"Password must contain at least {MinUniquePasswordChars} unique characters.");
        }
    }

    private static void AddError(Dictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var list))
        {
            list = [];
            errors[key] = list;
        }

        list.Add(message);
    }

    private static void ThrowIfHasErrors(Dictionary<string, List<string>> errors)
    {
        if (errors.Count == 0)
        {
            return;
        }

        var readOnly = errors.ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.ToArray(),
            StringComparer.Ordinal);

        throw new ValidationAppException("One or more validation errors occurred.", readOnly);
    }

    [GeneratedRegex("^[a-zA-Z0-9._-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex UserNameRegex();
}
