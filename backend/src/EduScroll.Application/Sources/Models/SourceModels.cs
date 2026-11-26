using EduScroll.Domain.Entities;

namespace EduScroll.Application.Sources.Models;

public sealed record WebFetchedArticle(
    string FinalUrl,
    string Title,
    string ExtractedText,
    string? ContentType);

public sealed record SourceListItem(
    Guid Id,
    string Title,
    SourceType SourceType,
    string? OriginalUrl,
    string? OriginalFileName,
    int ContentLength,
    string TextPreview,
    SourceStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

/// <summary>
/// Application-layer content and pagination limits bound from configuration.
/// </summary>
public sealed record SourceIngestionLimits(
    int MinExtractedTextLength = Source.MinExtractedTextLength,
    int MaxExtractedTextLength = Source.DefaultMaxExtractedTextLength,
    int PreviewMaxLength = 240,
    int DefaultPageSize = 20,
    int MaxPageSize = 50);
