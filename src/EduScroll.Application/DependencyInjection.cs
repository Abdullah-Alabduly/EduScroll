using System;
using Microsoft.Extensions.DependencyInjection;

namespace EduScroll.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        return services;
    }
}
