using EduScroll.Application.AiAnalysis.Ports;
using EduScroll.Application.AiAnalysis.Services;
using EduScroll.Application.Authentication.Ports;
using EduScroll.Application.Authentication.Services;
using EduScroll.Application.Sources.Ports;
using EduScroll.Application.Sources.Services;
using EduScroll.Application.VideoDiscovery.Ports;
using EduScroll.Application.VideoDiscovery.Services;
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
        services.TryAddScoped<IWhitelistCatalogSyncService, WhitelistCatalogSyncService>();

        return services;
    }
}
