using Microsoft.Extensions.DependencyInjection;
using RATools.Application.Applications;

namespace RATools.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IApplicationService, ApplicationService>();
        return services;
    }
}
