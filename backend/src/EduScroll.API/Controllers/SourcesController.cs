using EduScroll.API.Security;
using EduScroll.Application.Sources.Contracts;
using EduScroll.Application.Sources.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EduScroll.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1/sources")]
public sealed class SourcesController(ISourceService sourceService) : ControllerBase
{
    private const long MaxPdfJsonPayloadBytes = 1_048_576; // 1 MB JSON body cap for extracted UTF-8 text
    private const long MaxUrlJsonPayloadBytes = 16_384;    // 16 KB JSON body cap for URL submission

    [HttpPost]
    [RequestSizeLimit(MaxPdfJsonPayloadBytes)]
    [EndpointSummary("Create a PDF educational source from locally extracted text")]
    [EndpointDescription("Validates locally extracted UTF-8 text and metadata from a PDF document and persists the source under the authenticated user's ownership.")]
    [ProducesResponseType(typeof(SourceDetailDto), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    public async Task<IActionResult> CreatePdfSource(
        [FromBody] CreatePdfSourceRequest request,
        CancellationToken cancellationToken)
    {
        var ownerUserId = User.GetRequiredUserId();
        var created = await sourceService.CreatePdfSourceAsync(ownerUserId, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpPost("from-url")]
    [RequestSizeLimit(MaxUrlJsonPayloadBytes)]
    [EndpointSummary("Create a web article educational source from a public URL")]
    [EndpointDescription("Securely fetches a public HTTP/HTTPS article URL with SSRF protections, extracts clean readable text, and persists the source for the authenticated user.")]
    [ProducesResponseType(typeof(SourceDetailDto), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status413PayloadTooLarge, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout, "application/problem+json")]
    public async Task<IActionResult> CreateUrlSource(
        [FromBody] CreateUrlSourceRequest request,
        CancellationToken cancellationToken)
    {
        var ownerUserId = User.GetRequiredUserId();
        var created = await sourceService.CreateUrlSourceAsync(ownerUserId, request, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
    }

    [HttpGet]
    [EndpointSummary("List the authenticated user's sources")]
    [EndpointDescription("Returns a paginated list of lightweight source summaries owned exclusively by the authenticated user.")]
    [ProducesResponseType(typeof(PagedSourcesResponse), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> ListMySources(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var ownerUserId = User.GetRequiredUserId();
        var response = await sourceService.GetUserSourcesAsync(ownerUserId, page, pageSize, cancellationToken);
        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    [EndpointSummary("Get details for an owned source")]
    [EndpointDescription("Returns full source details including extracted text only when the source belongs to the authenticated user.")]
    [ProducesResponseType(typeof(SourceDetailDto), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var ownerUserId = User.GetRequiredUserId();
        var source = await sourceService.GetSourceByIdAsync(ownerUserId, id, cancellationToken);
        return Ok(source);
    }

    [HttpDelete("{id:guid}")]
    [EndpointSummary("Delete an owned source")]
    [EndpointDescription("Permanently deletes a source owned by the authenticated user.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken cancellationToken)
    {
        var ownerUserId = User.GetRequiredUserId();
        await sourceService.DeleteSourceAsync(ownerUserId, id, cancellationToken);
        return NoContent();
    }
}
