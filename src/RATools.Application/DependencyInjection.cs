using Microsoft.Extensions.DependencyInjection;
using RATools.Application.Auditing;
using RATools.Application.Applications;
using RATools.Application.Documents;
using RATools.Application.EctdStructure;
using RATools.Application.Publishing;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Standards;
using RATools.Application.Validation;

namespace RATools.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IApplicationDeletionTransaction, PassthroughApplicationDeletionTransaction>();
        services.AddScoped<IApplicationDeletionCoordinator, ApplicationDeletionCoordinator>();
        services.AddScoped<IApplicationService, ApplicationService>();
        services.AddScoped<IApplicationImportService, ApplicationImportService>();
        services.AddScoped<IApplicationPublishHistoryService, ApplicationPublishHistoryService>();
        services.AddScoped<ISequencePublishingMetadataService, SequencePublishingMetadataService>();
        services.AddSingleton<IEctdStructureService, EctdStructureService>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IDocumentPlacementService, DocumentPlacementService>();
        services.AddScoped<IBackboneService, BackboneService>();
        services.AddScoped<IEctdPackageModelBuilder, EctdPackageModelBuilder>();
        services.AddSingleton<IStandardsProfileProvider, FdaEctd322StandardsProfileProvider>();
        services.AddSingleton<IEctdWorkspacePathResolver, EctdWorkspacePathResolver>();
        services.AddSingleton<PublishOutputVerifier>();
        services.AddScoped<IPublishJobService, PublishJobService>();
        services.AddScoped<ISequenceValidationService, SequenceValidationService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        return services;
    }
}
