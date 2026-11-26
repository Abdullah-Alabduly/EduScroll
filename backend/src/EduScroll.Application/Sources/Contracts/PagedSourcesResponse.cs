namespace EduScroll.Application.Sources.Contracts;

public sealed record PagedSourcesResponse(
    IReadOnlyList<SourceSummaryDto> Items,
    int Page,
    int PageSize,
    int TotalCount,
    bool HasNextPage);
