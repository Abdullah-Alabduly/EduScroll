using EduScroll.Application.AiAnalysis.Models;

namespace EduScroll.Application.AiAnalysis.Ports;

/// <summary>
/// Provider-agnostic port for structured AI educational content analysis.
/// Concrete vendor/model HTTP details, prompt templates, and credentials live exclusively in Infrastructure.
/// </summary>
public interface IAiAnalysisProvider
{
    Task<AiAnalysisProviderResponse> AnalyzeAsync(
        AiAnalysisProviderRequest request,
        CancellationToken cancellationToken);
}
