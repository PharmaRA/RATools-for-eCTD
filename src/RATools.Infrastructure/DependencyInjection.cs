using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Abstractions.Security;
using RATools.Application.Abstractions.Storage;
using RATools.Application.Applications;
using RATools.Application.Publishing;
using RATools.Application.Validation;
using RATools.Infrastructure.Publishing;
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
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.Configure<SecurityOptions>(configuration.GetSection(SecurityOptions.SectionName));
        services.Configure<ValidationProfileOptions>(configuration.GetSection(ValidationProfileOptions.SectionName));
        services.AddSingleton<IBackboneFileWriter, LocalBackboneFileWriter>();
        services.AddSingleton<IPublishArtifactStore, LocalPublishArtifactStore>();
        services.AddSingleton<IPublishJobQueue, ChannelPublishJobQueue>();
        services.AddHostedService<PublishJobBackgroundService>();
        services.AddSingleton<IApplicationWorkspaceService, ApplicationWorkspaceService>();
        services.AddSingleton<IWorkspacePathPolicy, ConfiguredWorkspacePathPolicy>();
        services.AddSingleton<StartupConfigurationValidator>();
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
        return services;
    }
}
