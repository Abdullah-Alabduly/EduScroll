using EduScroll.Domain.Entities;

namespace EduScroll.Application.AiAnalysis.Ports;

public interface ISourceAnalysisRepository
{
    Task<SourceAnalysis?> GetBySourceIdForOwnerAsync(
        Guid sourceId,
        Guid ownerUserId,
        CancellationToken cancellationToken);

    Task SaveOrReplaceAsync(
        SourceAnalysis analysis,
        CancellationToken cancellationToken);
}
