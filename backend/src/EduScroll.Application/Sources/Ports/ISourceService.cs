using EduScroll.Application.Sources.Contracts;

namespace EduScroll.Application.Sources.Ports;

public interface ISourceService
{
    Task<SourceDetailDto> CreatePdfSourceAsync(
        Guid ownerUserId,
        CreatePdfSourceRequest request,
        CancellationToken cancellationToken);

    Task<SourceDetailDto> CreateUrlSourceAsync(
        Guid ownerUserId,
        CreateUrlSourceRequest request,
        CancellationToken cancellationToken);

    Task<PagedSourcesResponse> GetUserSourcesAsync(
        Guid ownerUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task<SourceDetailDto> GetSourceByIdAsync(
        Guid ownerUserId,
        Guid sourceId,
        CancellationToken cancellationToken);

    Task DeleteSourceAsync(
        Guid ownerUserId,
        Guid sourceId,
        CancellationToken cancellationToken);
}
