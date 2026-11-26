using EduScroll.Application.AiAnalysis.Contracts;

namespace EduScroll.Application.AiAnalysis.Ports;

public interface ISourceAnalysisService
{
    Task<SourceAnalysisDto> AnalyzeSourceAsync(
        Guid ownerUserId,
        Guid sourceId,
        AnalyzeSourceRequest? request,
        CancellationToken cancellationToken);

    Task<SourceAnalysisDto> GetAnalysisBySourceIdAsync(
        Guid ownerUserId,
        Guid sourceId,
        CancellationToken cancellationToken);
}
