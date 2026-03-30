using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Abstractions.Storage;
using RATools.Infrastructure.Publishing;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Storage;

namespace RATools.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BackboneOutputOptions>(configuration.GetSection(BackboneOutputOptions.SectionName));
        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.SectionName));
        services.AddSingleton<IBackboneFileWriter, LocalBackboneFileWriter>();
        services.AddSingleton<IFileStorage, LocalFileStorage>();

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
        return services;
    }
}
