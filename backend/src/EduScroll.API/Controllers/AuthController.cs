using EduScroll.API.Security;
using EduScroll.Application.Authentication.Contracts;
using EduScroll.Application.Authentication.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduScroll.API.Controllers;

[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    [AllowAnonymous]
    [EndpointSummary("Register a new user account")]
    [EndpointDescription("Creates a new learner account with ASP.NET Core Identity password hashing and unique email enforcement.")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> Register(
        [FromBody] RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var createdUser = await authService.RegisterAsync(request, cancellationToken);
        return CreatedAtAction(nameof(Me), createdUser);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    [EndpointSummary("Authenticate and issue JWT + refresh tokens")]
    [EndpointDescription("Validates user credentials, rejects inactive or locked accounts, and issues a JWT access token with a rotating refresh token.")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest request,
        CancellationToken cancellationToken)
    {
        var authResponse = await authService.LoginAsync(request, cancellationToken);
        return Ok(authResponse);
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    [EndpointSummary("Rotate refresh token and issue a new access token")]
    [EndpointDescription("Validates the supplied refresh token, revokes it, and issues a new JWT access token and replacement refresh token.")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshTokenRequest request,
        CancellationToken cancellationToken)
    {
        var authResponse = await authService.RefreshAsync(request, cancellationToken);
        return Ok(authResponse);
    }

    [HttpPost("logout")]
    [Authorize]
    [EndpointSummary("Revoke the current refresh token")]
    [EndpointDescription("Revokes the authenticated user's supplied refresh token so it can no longer be used to obtain new tokens.")]
    [ProducesResponseType(typeof(MessageResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.GetRequiredUserId();
        var response = await authService.LogoutAsync(userId, request, cancellationToken);
        return Ok(response);
    }

    [HttpGet("me")]
    [Authorize]
    [EndpointSummary("Get the authenticated user's profile")]
    [EndpointDescription("Returns non-sensitive profile metadata for the currently authenticated user.")]
    [ProducesResponseType(typeof(UserProfileDto), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden, "application/problem+json")]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        var userId = User.GetRequiredUserId();
        var profile = await authService.GetCurrentUserAsync(userId, cancellationToken);
        return Ok(profile);
    }
}
