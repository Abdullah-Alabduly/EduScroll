using EduScroll.Application.AiAnalysis.Ports;
using EduScroll.Application.AiAnalysis.Services;
using EduScroll.Application.Authentication.Ports;
using EduScroll.Application.Authentication.Services;
using EduScroll.Application.Sources.Ports;
using EduScroll.Application.Sources.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EduScroll.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers Application layer services.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IAuthService, AuthService>();
        services.TryAddScoped<ISourceService, SourceService>();
        services.TryAddScoped<ISourceAnalysisService, SourceAnalysisService>();

        return services;
    }
}
