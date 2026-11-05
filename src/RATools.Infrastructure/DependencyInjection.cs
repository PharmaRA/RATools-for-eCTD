using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Abstractions.Security;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Applications;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Validation.Pdf;
using RATools.Application.Validation;
using RATools.Infrastructure.Publishing;
using RATools.Infrastructure.Publishing.Validation.Pdf;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;
using RATools.Infrastructure.Validation;
using RATools.Application.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using RATools.Application.DocumentProcessing;
using RATools.Infrastructure.DocumentProcessing;

namespace RATools.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<QpdfProcessingOptions>().Bind(configuration.GetSection("PdfProcessing:Engine"))
            .Validate(options => { options.Validate(); return true; }, "Invalid qpdf engine settings.").ValidateOnStart();
        services.AddOptions<PdfProcessingExecutionOptions>().Bind(configuration.GetSection("PdfProcessing"))
            .Validate(options => options.MaxFileBytes > 0 && options.ExecutionTimeout > TimeSpan.Zero &&
                options.PollInterval > TimeSpan.Zero && options.HeartbeatInterval > TimeSpan.Zero &&
                options.LeaseDuration > options.HeartbeatInterval, "Invalid PDF processing execution settings.").ValidateOnStart();
        services.AddSingleton(provider => provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PdfProcessingExecutionOptions>>().Value);
        services.AddSingleton<IPdfProcessingEngine>(provider => new QpdfProcessingEngine(
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<QpdfProcessingOptions>>().Value));
        services.AddSingleton<ProcessingFileStore>();
        services.AddHostedService<DocumentProcessingBackgroundService>();
        services.AddOptions<PackageValidationOptions>().Bind(configuration.GetSection(PackageValidationOptions.SectionName))
            .Validate(value => !string.IsNullOrWhiteSpace(value.StorageRoot) && value.PollInterval > TimeSpan.Zero &&
                value.HeartbeatInterval > TimeSpan.Zero && value.LeaseDuration > value.HeartbeatInterval &&
                value.ExecutionTimeout > TimeSpan.Zero && value.MaxAttempts > 0, "Invalid independent validation execution options.")
            .Validate(value => { value.ReadLimits.Validate(); return true; }, "Invalid independent validation read limits.").ValidateOnStart();
        services.AddSingleton<IPackageInputReader>(provider => new PackageInputReader(Path.Combine(
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PackageValidationOptions>>().Value.StorageRoot, "captures")));
        services.AddSingleton<IPackagePdfInspector, PdfPigPackageInspector>();
        services.AddSingleton<PackageDeliveryInspector>();
        services.AddScoped<PackageValidationInputRegistry>();
        services.AddScoped<FinalizedHistoryResolver>();
        services.AddScoped<PackageValidationService>();
        services.AddHostedService<PackageValidationBackgroundService>();
        services.Configure<BackboneOutputOptions>(configuration.GetSection(BackboneOutputOptions.SectionName));
        services.AddOptions<PublishJobExecutionOptions>()
            .Bind(configuration.GetSection(PublishJobExecutionOptions.SectionName))
            .Validate(options => options.ExecutionTimeout > TimeSpan.Zero,
                "PublishJobs:ExecutionTimeout must be greater than zero.")
            .Validate(options => options.PollInterval > TimeSpan.Zero,
                "PublishJobs:PollInterval must be greater than zero.")
            .Validate(options => options.LeaseDuration > TimeSpan.Zero,
                "PublishJobs:LeaseDuration must be greater than zero.")
            .Validate(options => options.HeartbeatInterval > TimeSpan.Zero
                && options.HeartbeatInterval < options.LeaseDuration,
                "PublishJobs:HeartbeatInterval must be greater than zero and less than LeaseDuration.")
            .Validate(options => options.RetryDelay >= TimeSpan.Zero,
                "PublishJobs:RetryDelay must not be negative.")
            .Validate(options => options.MaxAttempts > 0,
                "PublishJobs:MaxAttempts must be greater than zero.")
            .ValidateOnStart();
        services.AddOptions<MonitoringOptions>()
            .Bind(configuration.GetSection(MonitoringOptions.SectionName))
            .Validate(options => options.QueueSampleInterval > TimeSpan.Zero,
                "Monitoring:QueueSampleInterval must be greater than zero.")
            .ValidateOnStart();
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.Configure<DeploymentOptions>(configuration.GetSection(DeploymentOptions.SectionName));
        services.Configure<ValidationProfileOptions>(configuration.GetSection(ValidationProfileOptions.SectionName));
        services.AddSingleton<IBackboneFileWriter, LocalBackboneFileWriter>();
        services.AddSingleton<RATools.Application.Finalization.IReleaseCaptureStore, LocalReleaseCaptureStore>();
        services.AddSingleton<IPublishArtifactStore, LocalPublishArtifactStore>();
        services.AddSingleton<IPdfInspector, PdfPigPdfInspector>();
        services.AddSingleton<IPublishJobQueue, ChannelPublishJobQueue>();
        // 注册顺序即启动顺序：先原子回收租约已过期的 Running 作业，再启动队列消费。
        // 是否真正执行回收由服务在 StartAsync 里按运行时配置判断（注册阶段读不到
        // WebApplicationFactory 等宿主在 ConfigureAppConfiguration 中的覆盖值）。
        services.AddHostedService<NodeFileMoveRecoveryService>();
        services.AddHostedService<StalePublishJobRecoveryService>();
        services.AddHostedService<PublishJobBackgroundService>();
        services.AddHostedService<PublishQueueMetricsService>();
        services.AddSingleton<IApplicationWorkspaceService, ApplicationWorkspaceService>();
        services.AddSingleton<IWorkspacePathPolicy, ConfiguredWorkspacePathPolicy>();
        services.AddSingleton<StartupConfigurationValidator>();
        services.AddSingleton<LocalOnlyDeploymentValidator>();
        services.AddSingleton<LocalOnlyInstanceLock>();
        services.AddSingleton<IServerDirectoryBrowser, LocalServerDirectoryBrowser>();
        services.AddSingleton<IWorkspaceDeletionService, WorkspaceDeletionService>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddSingleton<IValidationProfileProvider, ConfigurationValidationProfileProvider>();

        var provider = configuration.GetValue<string>("Persistence:Provider") ?? "PostgreSql";
        if (string.Equals(provider, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IDocumentProcessingStore, InMemoryDocumentProcessingStore>();
            services.AddSingleton<IPackageValidationStore, InMemoryPackageValidationStore>();
            services.AddSingleton<RATools.Application.Finalization.IFinalizationStore, InMemoryFinalizationStore>();
            services.AddSingleton<IApplicationRepository, InMemoryApplicationRepository>();
            services.AddSingleton<IWorkspaceRevisionStore, InMemoryWorkspaceRevisionStore>();
            services.AddSingleton<InMemoryCtdNodeRepository>();
            services.AddSingleton<ICtdNodeRepository>(provider => provider.GetRequiredService<InMemoryCtdNodeRepository>());
            services.AddSingleton<IApplicationImportStore, InMemoryApplicationImportStore>();
            services.AddScoped<IPersistenceTransaction, InMemoryPersistenceTransaction>();
            services.AddSingleton<IDocumentRepository, InMemoryDocumentRepository>();
            services.AddSingleton<IDocumentPlacementRepository, InMemoryDocumentPlacementRepository>();
            services.AddSingleton<IPublishJobRepository, InMemoryPublishJobRepository>();
            services.AddSingleton<IAuditLogRepository, InMemoryAuditLogRepository>();
            return services;
        }

        var connectionString = configuration.GetConnectionString("PostgreSql");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Connection string 'PostgreSql' is not configured.");
        }

        services.AddDbContext<RAToolsDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<IDocumentProcessingStore, EfCoreDocumentProcessingStore>();
        services.AddScoped<IPackageValidationStore, EfCorePackageValidationStore>();
        services.AddScoped<RATools.Application.Finalization.IFinalizationStore, EfCoreFinalizationStore>();
        services.AddScoped<IApplicationRepository, EfCoreApplicationRepository>();
        services.AddScoped<IDocumentRepository, EfCoreDocumentRepository>();
        services.AddScoped<IDocumentPlacementRepository, EfCoreDocumentPlacementRepository>();
        services.AddScoped<ICtdNodeRepository, EfCoreCtdNodeRepository>();
        services.AddScoped<IApplicationImportStore, EfCoreApplicationImportStore>();
        services.AddScoped<IWorkspaceRevisionStore, EfCoreWorkspaceRevisionStore>();
        services.AddScoped<IPublishJobRepository, EfCorePublishJobRepository>();
        services.AddScoped<IAuditLogRepository, EfCoreAuditLogRepository>();
        services.AddScoped<IApplicationDeletionTransaction, EfCoreApplicationDeletionTransaction>();
        services.AddScoped<IPersistenceTransaction, EfCorePersistenceTransaction>();
        return services;
    }
}
