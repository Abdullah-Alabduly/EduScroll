using EduScroll.API.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace EduScroll.API.Controllers;

[ApiController]
[Route("health")]
public sealed class HealthController(HealthCheckService healthCheckService) : ControllerBase
{
    [HttpGet("live")]
    [EndpointSummary("Liveness probe")]
    [EndpointDescription("Indicates whether the EduScroll API process is alive and able to handle HTTP requests.")]
    [ProducesResponseType(typeof(HealthStatusResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Live(CancellationToken cancellationToken)
    {
        var report = await healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains("live"),
            cancellationToken);

        return ToActionResult(report);
    }

    [HttpGet("ready")]
    [EndpointSummary("Readiness probe")]
    [EndpointDescription("Indicates whether the EduScroll API foundation is ready to accept traffic based on dependencies active in Sprint 1.")]
    [ProducesResponseType(typeof(HealthStatusResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
    public async Task<IActionResult> Ready(CancellationToken cancellationToken)
    {
        var report = await healthCheckService.CheckHealthAsync(
            registration => registration.Tags.Contains("ready"),
            cancellationToken);

        return ToActionResult(report);
    }

    private IActionResult ToActionResult(HealthReport report)
    {
        if (report.Status == HealthStatus.Healthy)
        {
            return Ok(new HealthStatusResponse(report.Status.ToString()));
        }

        return Problem(
            title: "Service unavailable.",
            detail: "One or more readiness checks reported an unhealthy state.",
            statusCode: StatusCodes.Status503ServiceUnavailable,
            type: "https://tools.ietf.org/html/rfc9110#section-15.6.4");
    }
}
