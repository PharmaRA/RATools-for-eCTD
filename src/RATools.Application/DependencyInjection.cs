using Microsoft.Extensions.DependencyInjection;
using RATools.Application.Auditing;
using RATools.Application.Applications;
using RATools.Application.Documents;
using RATools.Application.Publishing;

namespace RATools.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IApplicationService, ApplicationService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IDocumentPlacementService, DocumentPlacementService>();
        services.AddScoped<IBackboneService, BackboneService>();
        services.AddScoped<IPublishJobService, PublishJobService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        return services;
    }
}
