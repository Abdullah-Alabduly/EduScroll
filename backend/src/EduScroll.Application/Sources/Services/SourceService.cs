using EduScroll.Application.Exceptions;
using EduScroll.Application.Sources.Contracts;
using EduScroll.Application.Sources.Models;
using EduScroll.Application.Sources.Ports;
using EduScroll.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace EduScroll.Application.Sources.Services;

public sealed class SourceService(
    ISourceRepository sourceRepository,
    IWebContentFetcher webContentFetcher,
    SourceIngestionLimits limits,
    TimeProvider timeProvider,
    ILogger<SourceService> logger) : ISourceService
{
    public async Task<SourceDetailDto> CreatePdfSourceAsync(
        Guid ownerUserId,
        CreatePdfSourceRequest request,
        CancellationToken cancellationToken)
    {
        EnsureValidOwner(ownerUserId);

        var (title, normalizedText, fileName) =
            SourceRequestValidator.ValidateAndNormalizePdfRequest(request, limits);

        var now = timeProvider.GetUtcNow();
        var source = Source.CreatePdf(
            ownerUserId,
            title,
            normalizedText,
            now,
            fileName,
            limits.MinExtractedTextLength,
            limits.MaxExtractedTextLength);

        await sourceRepository.AddAsync(source, cancellationToken);

        logger.LogInformation(
            "PDF source created. SourceId={SourceId} OwnerUserId={OwnerUserId} ContentLength={ContentLength}",
            source.Id,
            source.OwnerUserId,
            source.ContentLength);

        return MapDetail(source);
    }

    public async Task<SourceDetailDto> CreateUrlSourceAsync(
        Guid ownerUserId,
        CreateUrlSourceRequest request,
        CancellationToken cancellationToken)
    {
        EnsureValidOwner(ownerUserId);

        var requestedUri = SourceRequestValidator.ValidateAndParseUrlRequest(request);
        var fetched = await webContentFetcher.FetchAndExtractAsync(requestedUri, cancellationToken);

        var normalizedText = SourceTextNormalizer.Normalize(fetched.ExtractedText);
        if (string.IsNullOrWhiteSpace(normalizedText) ||
            normalizedText.Length < limits.MinExtractedTextLength)
        {
            throw new UnprocessableEntityAppException(
                "Could not extract enough readable article text from this web page.",
                "ARTICLE_EXTRACTION_EMPTY");
        }

        if (normalizedText.Length > limits.MaxExtractedTextLength)
        {
            throw new PayloadTooLargeAppException(
                $"Extracted article text exceeds the maximum supported length of {limits.MaxExtractedTextLength:N0} characters.",
                "SOURCE_TEXT_TOO_LONG");
        }

        var normalizedTitle = SourceTextNormalizer.NormalizeTitle(fetched.Title);
        if (string.IsNullOrWhiteSpace(normalizedTitle))
        {
            normalizedTitle = requestedUri.Host;
        }

        if (normalizedTitle.Length > Source.MaxTitleLength)
        {
            normalizedTitle = normalizedTitle[..Source.MaxTitleLength].TrimEnd();
        }

        var finalUrl = string.IsNullOrWhiteSpace(fetched.FinalUrl)
            ? requestedUri.AbsoluteUri
            : fetched.FinalUrl.Trim();
        if (finalUrl.Length > Source.MaxOriginalUrlLength)
        {
            finalUrl = requestedUri.AbsoluteUri;
        }

        var now = timeProvider.GetUtcNow();
        var source = Source.CreateWebArticle(
            ownerUserId,
            normalizedTitle,
            finalUrl,
            normalizedText,
            now,
            limits.MinExtractedTextLength,
            limits.MaxExtractedTextLength);

        await sourceRepository.AddAsync(source, cancellationToken);

        logger.LogInformation(
            "Web article source created. SourceId={SourceId} OwnerUserId={OwnerUserId} ContentLength={ContentLength}",
            source.Id,
            source.OwnerUserId,
            source.ContentLength);

        return MapDetail(source);
    }

    public async Task<PagedSourcesResponse> GetUserSourcesAsync(
        Guid ownerUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        EnsureValidOwner(ownerUserId);

        var (validPage, validPageSize) =
            SourceRequestValidator.ValidatePagination(page, pageSize, limits);

        var (items, totalCount) = await sourceRepository.GetPagedForOwnerAsync(
            ownerUserId,
            validPage,
            validPageSize,
            limits.PreviewMaxLength,
            cancellationToken);

        var dtos = items
            .Select(item => new SourceSummaryDto(
                item.Id,
                item.Title,
                item.SourceType.ToString(),
                item.OriginalUrl,
                item.OriginalFileName,
                item.ContentLength,
                SourceTextNormalizer.BuildPreview(item.TextPreview, limits.PreviewMaxLength),
                item.Status.ToString(),
                item.CreatedAtUtc,
                item.UpdatedAtUtc))
            .ToArray();

        var hasNextPage = ((long)validPage * validPageSize) < totalCount;

        return new PagedSourcesResponse(
            dtos,
            validPage,
            validPageSize,
            totalCount,
            hasNextPage);
    }

    public async Task<SourceDetailDto> GetSourceByIdAsync(
        Guid ownerUserId,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        EnsureValidOwner(ownerUserId);

        if (sourceId == Guid.Empty)
        {
            throw new NotFoundAppException("The requested source was not found.", "SOURCE_NOT_FOUND");
        }

        var source = await sourceRepository.GetByIdForOwnerAsync(sourceId, ownerUserId, cancellationToken);
        if (source is null || !source.IsOwnedBy(ownerUserId))
        {
            throw new NotFoundAppException("The requested source was not found.", "SOURCE_NOT_FOUND");
        }

        return MapDetail(source);
    }

    public async Task DeleteSourceAsync(
        Guid ownerUserId,
        Guid sourceId,
        CancellationToken cancellationToken)
    {
        EnsureValidOwner(ownerUserId);

        if (sourceId == Guid.Empty)
        {
            throw new NotFoundAppException("The requested source was not found.", "SOURCE_NOT_FOUND");
        }

        var deleted = await sourceRepository.DeleteForOwnerAsync(sourceId, ownerUserId, cancellationToken);
        if (!deleted)
        {
            throw new NotFoundAppException("The requested source was not found.", "SOURCE_NOT_FOUND");
        }

        logger.LogInformation(
            "Source deleted. SourceId={SourceId} OwnerUserId={OwnerUserId}",
            sourceId,
            ownerUserId);
    }

    private static void EnsureValidOwner(Guid ownerUserId)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new UnauthorizedAppException("Authenticated user identifier is missing or invalid.", "UNAUTHORIZED");
        }
    }

    private static SourceDetailDto MapDetail(Source source) =>
        new(
            source.Id,
            source.Title,
            source.SourceType.ToString(),
            source.OriginalUrl,
            source.OriginalFileName,
            source.ExtractedText,
            source.ContentLength,
            source.Status.ToString(),
            source.CreatedAtUtc,
            source.UpdatedAtUtc);
}
