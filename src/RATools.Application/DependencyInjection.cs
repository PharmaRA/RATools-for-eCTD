using Microsoft.Extensions.DependencyInjection;
using RATools.Application.Auditing;
using RATools.Application.Applications;
using RATools.Application.Documents;
using RATools.Application.Publishing;
using RATools.Application.Validation;

namespace RATools.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IApplicationService, ApplicationService>();
        services.AddScoped<IApplicationImportService, ApplicationImportService>();
        services.AddScoped<IApplicationPublishHistoryService, ApplicationPublishHistoryService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IDocumentPlacementService, DocumentPlacementService>();
        services.AddScoped<IBackboneService, BackboneService>();
        services.AddSingleton<PublishOutputVerifier>();
        services.AddScoped<IPublishJobService, PublishJobService>();
        services.AddScoped<ISequenceValidationService, SequenceValidationService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        return services;
    }
}
