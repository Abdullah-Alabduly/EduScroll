using EduScroll.API.Security;
using EduScroll.Application.VideoDiscovery.Contracts;
using EduScroll.Application.VideoDiscovery.Ports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace EduScroll.API.Controllers;

[ApiController]
[Authorize]
[Route("api/v1")]
public sealed class VideoDiscoveryController(IVideoDiscoveryService videoDiscoveryService) : ControllerBase
{
    private const long MaxDiscoverRequestBytes = 4_096;
    private const long MaxCreateChannelRequestBytes = 8_192;
    private const long MaxSyncRequestBytes = 4_096;

    [HttpPost("sources/{sourceId:guid}/match-videos")]
    [RequestSizeLimit(MaxDiscoverRequestBytes)]
    [EndpointSummary("Analyze a source and match real educational YouTube video URLs")]
    [EndpointDescription("Reuses or generates AI content analysis for an owned source (via the configured AI provider such as Gemini), then runs Database-First YouTube video discovery and returns matched YouTube watch URLs with metadata.")]
    [ProducesResponseType(typeof(SourceVideoMatchResultDto), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status504GatewayTimeout, "application/problem+json")]
    public async Task<IActionResult> MatchVideosForSource(
        Guid sourceId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] MatchSourceVideosRequest? request,
        CancellationToken cancellationToken)
    {
        var ownerUserId = User.GetRequiredUserId();
        var result = await videoDiscoveryService.MatchVideosForSourceAsync(
            ownerUserId,
            sourceId,
            request,
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("sources/{sourceId:guid}/discover")]
    [RequestSizeLimit(MaxDiscoverRequestBytes)]
    [EndpointSummary("Discover educational video candidates for an analyzed source")]
    [EndpointDescription("Runs Database-First video discovery against the local SQL Server catalog using the owned source's latest AI analysis queries, falling back to YouTube search.list with write-through caching when below threshold.")]
    [ProducesResponseType(typeof(VideoDiscoveryResultDto), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> DiscoverForSource(
        Guid sourceId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DiscoverSourceVideosRequest? request,
        CancellationToken cancellationToken)
    {
        var ownerUserId = User.GetRequiredUserId();
        var result = await videoDiscoveryService.DiscoverForSourceAsync(
            ownerUserId,
            sourceId,
            request,
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("discovery/channels")]
    [EndpointSummary("List whitelisted educational YouTube channels")]
    [EndpointDescription("Returns curated whitelisted YouTube channels with optional language ('en'/'ar') and active status filters.")]
    [ProducesResponseType(typeof(IReadOnlyList<WhitelistedChannelDto>), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> ListChannels(
        [FromQuery] string? language = null,
        [FromQuery] bool? isActive = null,
        CancellationToken cancellationToken = default)
    {
        var channels = await videoDiscoveryService.ListWhitelistedChannelsAsync(
            language,
            isActive,
            cancellationToken);

        return Ok(channels);
    }

    [HttpPost("discovery/channels")]
    [RequestSizeLimit(MaxCreateChannelRequestBytes)]
    [EndpointSummary("Add a whitelisted educational YouTube channel")]
    [EndpointDescription("Validates and persists a curated educational YouTube channel for Database-First RSS catalog ingestion.")]
    [ProducesResponseType(typeof(WhitelistedChannelDto), StatusCodes.Status201Created, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<IActionResult> CreateChannel(
        [FromBody] CreateWhitelistedChannelRequest request,
        CancellationToken cancellationToken)
    {
        var created = await videoDiscoveryService.CreateWhitelistedChannelAsync(request, cancellationToken);
        return CreatedAtAction(nameof(ListChannels), new { language = created.Language }, created);
    }

    [HttpPost("discovery/whitelist/sync")]
    [RequestSizeLimit(MaxSyncRequestBytes)]
    [EndpointSummary("Sync active whitelisted YouTube channels into the local catalog")]
    [EndpointDescription("Fetches Atom RSS feeds for active whitelisted channels, enriches video metadata in batches of 50, and upserts validated CatalogVideos into SQL Server.")]
    [ProducesResponseType(typeof(WhitelistSyncSummaryDto), StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<IActionResult> SyncWhitelist(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SyncWhitelistRequest? request,
        [FromQuery] string? language = null,
        CancellationToken cancellationToken = default)
    {
        var effectiveLanguage = !string.IsNullOrWhiteSpace(request?.Language)
            ? request.Language
            : language;

        var summary = await videoDiscoveryService.SyncWhitelistAsync(effectiveLanguage, cancellationToken);
        return Ok(summary);
    }
}
