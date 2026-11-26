namespace EduScroll.Application.Sources.Contracts;

public sealed record SourceSummaryDto(
    Guid Id,
    string Title,
    string SourceType,
    string? OriginalUrl,
    string? OriginalFileName,
    int ContentLength,
    string TextPreview,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
