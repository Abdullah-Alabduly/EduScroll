using Microsoft.Extensions.DependencyInjection;

namespace EduScroll.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers Application layer services.
    /// Feature-specific use cases and validators will be registered here in subsequent sprints.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
