using EduScroll.API.Security;
using EduScroll.Application.AiAnalysis.Contracts;
using EduScroll.Application.AiAnalysis.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EduScroll.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/sources")]
public sealed class SourceAnalysisController(ISourceAnalysisService sourceAnalysisService) : ControllerBase
{
    private const long MaxAnalyzeRequestBytes = 4_096;

    [HttpPost("{id:guid}/analyze")]
    [RequestSizeLimit(MaxAnalyzeRequestBytes)]
    [EndpointSummary("Run or retrieve AI content analysis for an owned source")]
    [EndpointDescription("Transforms a user-owned source's extracted text into validated structured educational metadata (subject, topic, summary, topics/subtopics, concepts, keywords, and search queries).")]
    [ProducesResponseType(typeof(SourceAnalysisDto), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout, "application/problem+json")]
    public async Task<IActionResult> Analyze(
        Guid id,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] AnalyzeSourceRequest? request,
        CancellationToken cancellationToken)
    {
        var ownerUserId = User.GetRequiredUserId();
        var analysis = await sourceAnalysisService.AnalyzeSourceAsync(
            ownerUserId,
            id,
            request,
            cancellationToken);

        return Ok(analysis);
    }

    [HttpGet("{id:guid}/analysis")]
    [EndpointSummary("Get the persisted AI content analysis for an owned source")]
    [EndpointDescription("Returns the previously generated and validated AI content analysis for a source owned by the authenticated user.")]
    [ProducesResponseType(typeof(SourceAnalysisDto), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetAnalysis(
        Guid id,
        CancellationToken cancellationToken)
    {
        var ownerUserId = User.GetRequiredUserId();
        var analysis = await sourceAnalysisService.GetAnalysisBySourceIdAsync(
            ownerUserId,
            id,
            cancellationToken);

        return Ok(analysis);
    }
}
