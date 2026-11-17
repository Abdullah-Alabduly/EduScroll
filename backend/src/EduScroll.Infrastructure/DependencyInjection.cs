using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EduScroll.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers Infrastructure layer services.
    /// Database (PostgreSQL/EF Core), Identity/JWT, Web Content Acquisition, AI Provider,
    /// and YouTube Data API integrations will be wired here in Sprints 2–6.
    /// </summary>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton(TimeProvider.System);

        return services;
    }
}
