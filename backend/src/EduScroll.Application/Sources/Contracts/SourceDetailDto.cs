namespace EduScroll.Application.Sources.Contracts;

public sealed record SourceDetailDto(
    Guid Id,
    string Title,
    string SourceType,
    string? OriginalUrl,
    string? OriginalFileName,
    string ExtractedText,
    int ContentLength,
    string Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);
