using Microsoft.Extensions.DependencyInjection;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Auditing;
using RATools.Application.Applications;
using RATools.Application.Documents;
using RATools.Application.EctdStructure;
using RATools.Application.Publishing;
using RATools.Application.Publishing.EuRegional;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.Validation;
using RATools.Application.Persistence;
using RATools.Application.Standards;
using RATools.Application.Validation;
using RATools.Application.Validation.Rules;
using RATools.Application.Validation.Rules.Pdf;

namespace RATools.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IPersistenceTransaction, PassthroughPersistenceTransaction>();
        services.AddScoped<IApplicationDeletionTransaction, PassthroughApplicationDeletionTransaction>();
        services.AddScoped<IApplicationDeletionCoordinator, ApplicationDeletionCoordinator>();
        services.AddScoped<IApplicationService, ApplicationService>();
        services.AddScoped<IApplicationImportService, ApplicationImportService>();
        services.AddScoped<IApplicationPublishHistoryService, ApplicationPublishHistoryService>();
        services.AddScoped<ISequencePublishingMetadataService, SequencePublishingMetadataService>();
        services.AddSingleton<IEctdStructureService, EctdStructureService>();
        services.AddSingleton<IDocumentStorageBoundary, DocumentStorageBoundary>();
        services.AddScoped<IDocumentService, DocumentService>();
        services.AddScoped<IDocumentPlacementService, DocumentPlacementService>();
        services.AddScoped<IBackboneService, BackboneService>();
        services.AddSingleton<IIchIndexXmlWriter, IchIndexXmlWriter>();
        services.AddSingleton<IUsRegionalXmlWriter, UsRegionalXmlWriter>();
        services.AddSingleton<IEuRegionalXmlWriter, EuRegionalXmlWriter>();
        services.AddSingleton<IRegionalBackboneWriter, UsRegionalBackboneWriter>();
        services.AddSingleton<IRegionalBackboneWriter, EuRegionalBackboneWriter>();
        services.AddSingleton<IRegionalBackboneWriterRegistry, RegionalBackboneWriterRegistry>();
        services.AddSingleton<IEctdXmlValidator, EctdXmlValidator>();
        services.AddScoped<IEctdPackageModelBuilder, EctdPackageModelBuilder>();
        services.AddSingleton<FdaEctd322StandardsProfileProvider>();
        services.AddSingleton<EuEctd322StandardsProfileProvider>();
        services.AddSingleton<IStandardsProfileProvider>(provider =>
            new CompositeStandardsProfileProvider(
            [
                provider.GetRequiredService<FdaEctd322StandardsProfileProvider>(),
                provider.GetRequiredService<EuEctd322StandardsProfileProvider>()
            ]));
        services.AddSingleton<IEctdValidationRule, FileNamingConventionRule>();
        services.AddSingleton<IEctdValidationRule, PdfComplianceRule>();
        services.AddSingleton<IEctdValidationRuleSetProvider, RegionalEctdRuleSetProvider>();
        services.AddSingleton<IEctdValidationEngine, EctdValidationEngine>();
        services.AddSingleton<IEctdWorkspacePathResolver, EctdWorkspacePathResolver>();
        services.AddSingleton<PublishOutputVerifier>();
        services.AddSingleton<PublishArtifactResolver>();
        services.AddSingleton<PublishReportStore>();
        services.AddScoped<IPublishJobService, PublishJobService>();
        services.AddScoped<ISequenceValidationService, SequenceValidationService>();
        services.AddScoped<IPublishReadinessService, PublishReadinessService>();
        services.AddScoped<IAuditLogService, AuditLogService>();
        return services;
    }
}
