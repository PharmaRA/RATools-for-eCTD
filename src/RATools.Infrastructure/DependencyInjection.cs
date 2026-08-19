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

namespace RATools.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
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
        services.AddSingleton<IPublishArtifactStore, LocalPublishArtifactStore>();
        services.AddSingleton<IPdfInspector, PdfPigPdfInspector>();
        services.AddSingleton<IPublishJobQueue, ChannelPublishJobQueue>();
        // 注册顺序即启动顺序：先原子回收租约已过期的 Running 作业，再启动队列消费。
        // 是否真正执行回收由服务在 StartAsync 里按运行时配置判断（注册阶段读不到
        // WebApplicationFactory 等宿主在 ConfigureAppConfiguration 中的覆盖值）。
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
            services.AddSingleton<IApplicationRepository, InMemoryApplicationRepository>();
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
        services.AddScoped<IApplicationRepository, EfCoreApplicationRepository>();
        services.AddScoped<IDocumentRepository, EfCoreDocumentRepository>();
        services.AddScoped<IDocumentPlacementRepository, EfCoreDocumentPlacementRepository>();
        services.AddScoped<IPublishJobRepository, EfCorePublishJobRepository>();
        services.AddScoped<IAuditLogRepository, EfCoreAuditLogRepository>();
        services.AddScoped<IApplicationDeletionTransaction, EfCoreApplicationDeletionTransaction>();
        services.AddScoped<IPersistenceTransaction, EfCorePersistenceTransaction>();
        return services;
    }
}
