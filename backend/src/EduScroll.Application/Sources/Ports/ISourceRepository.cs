using EduScroll.Application.Sources.Models;
using EduScroll.Domain.Entities;

namespace EduScroll.Application.Sources.Ports;

public interface ISourceRepository
{
    Task AddAsync(Source source, CancellationToken cancellationToken);

    Task<Source?> GetByIdForOwnerAsync(
        Guid sourceId,
        Guid ownerUserId,
        CancellationToken cancellationToken);

    Task<(IReadOnlyList<SourceListItem> Items, int TotalCount)> GetPagedForOwnerAsync(
        Guid ownerUserId,
        int page,
        int pageSize,
        int previewMaxLength,
        CancellationToken cancellationToken);

    Task<bool> DeleteForOwnerAsync(
        Guid sourceId,
        Guid ownerUserId,
        CancellationToken cancellationToken);
}
