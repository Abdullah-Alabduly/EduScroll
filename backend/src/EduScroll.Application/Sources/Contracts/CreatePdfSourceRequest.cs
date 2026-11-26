namespace EduScroll.Application.Sources.Contracts;

public sealed record CreatePdfSourceRequest(
    string Title,
    string SourceType,
    string ExtractedText,
    string? FileName = null);
