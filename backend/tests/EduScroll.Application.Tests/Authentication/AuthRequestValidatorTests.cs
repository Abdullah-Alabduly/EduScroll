using EduScroll.Application.Authentication.Contracts;
using EduScroll.Application.Authentication.Services;
using EduScroll.Application.Exceptions;

namespace EduScroll.Application.Tests.Authentication;

public sealed class AuthRequestValidatorTests
{
    [Fact]
    public void ValidateRegister_ShouldTrimNormalizedFields_WithoutTrimmingPassword()
    {
        var request = new RegisterRequest(
            "  Ada Lovelace  ",
            "  ada.lovelace  ",
            "  ada@example.com  ",
            "StrongPass123!");

        var (fullName, userName, email, password) = AuthRequestValidator.ValidateRegister(request);

        Assert.Equal("Ada Lovelace", fullName);
        Assert.Equal("ada.lovelace", userName);
        Assert.Equal("ada@example.com", email);
        Assert.Equal("StrongPass123!", password);
    }

    [Theory]
    [InlineData("", "valid_user", "user@example.com", "StrongPass123!", "fullName")]
    [InlineData("A", "valid_user", "user@example.com", "StrongPass123!", "fullName")]
    [InlineData("Valid Name", "ab", "user@example.com", "StrongPass123!", "userName")]
    [InlineData("Valid Name", "bad user!", "user@example.com", "StrongPass123!", "userName")]
    [InlineData("Valid Name", "valid_user", "not-an-email", "StrongPass123!", "email")]
    [InlineData("Valid Name", "valid_user", "user@localhost", "StrongPass123!", "email")]
    [InlineData("Valid Name", "valid_user", "user@domain.", "StrongPass123!", "email")]
    [InlineData("Valid Name", "valid_user", "user@.com", "StrongPass123!", "email")]
    [InlineData("Valid Name", "valid_user", "user@domain..com", "StrongPass123!", "email")]
    [InlineData("Valid Name", "valid_user", "user@-bad.com", "StrongPass123!", "email")]
    [InlineData("Valid Name", "valid_user", "user@domain.c", "StrongPass123!", "email")]
    [InlineData("Valid Name", "valid_user", "user..name@example.com", "StrongPass123!", "email")]
    [InlineData("Valid Name", "valid_user", "user@example.com", "short1!", "password")]
    [InlineData("Valid Name", "valid_user", "user@example.com", "lowercase123!", "password")]
    [InlineData("Valid Name", "valid_user", "user@example.com", "UPPERCASE123!", "password")]
    [InlineData("Valid Name", "valid_user", "user@example.com", "NoDigitsHere!", "password")]
    [InlineData("Valid Name", "valid_user", "user@example.com", "NoSpecial1234", "password")]
    [InlineData("Valid Name", "valid_user", "user@example.com", "Aa1!Aa1!", "password")]
    public void ValidateRegister_ShouldThrowValidationAppException_WhenInputIsInvalid(
        string fullName,
        string userName,
        string email,
        string password,
        string expectedField)
    {
        var request = new RegisterRequest(fullName, userName, email, password);

        var ex = Assert.Throws<ValidationAppException>(() => AuthRequestValidator.ValidateRegister(request));

        Assert.Equal("VALIDATION_FAILED", ex.ErrorCode);
        Assert.True(ex.Errors.ContainsKey(expectedField));
    }

    [Theory]
    [InlineData("", "password123", "email")]
    [InlineData("bad-email", "password123", "email")]
    [InlineData("user@example.com", "", "password")]
    [InlineData("user@example.com", "   ", "password")]
    public void ValidateLogin_ShouldThrowValidationAppException_WhenInputIsInvalid(
        string email,
        string password,
        string expectedField)
    {
        var request = new LoginRequest(email, password);

        var ex = Assert.Throws<ValidationAppException>(() => AuthRequestValidator.ValidateLogin(request));

        Assert.True(ex.Errors.ContainsKey(expectedField));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ValidateRefreshTokenValue_ShouldThrowValidationAppException_WhenEmpty(string token)
    {
        var ex = Assert.Throws<ValidationAppException>(
            () => AuthRequestValidator.ValidateRefreshTokenValue(token));

        Assert.True(ex.Errors.ContainsKey("refreshToken"));
    }

    [Fact]
    public void ValidateRefreshTokenValue_ShouldThrowValidationAppException_WhenTooLong()
    {
        var oversized = new string('a', AuthRequestValidator.MaxRefreshTokenLength + 1);

        var ex = Assert.Throws<ValidationAppException>(
            () => AuthRequestValidator.ValidateRefreshTokenValue(oversized));

        Assert.True(ex.Errors.ContainsKey("refreshToken"));
    }
}
