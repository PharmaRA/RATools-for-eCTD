using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class RAToolsDbContextFactory : IDesignTimeDbContextFactory<RAToolsDbContext>
{
    public RAToolsDbContext CreateDbContext(string[] args)
    {
        var apiPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "RATools.Api"));

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiPath)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("PostgreSql")
            ?? "Host=localhost;Port=5432;Database=ratools;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<RAToolsDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new RAToolsDbContext(optionsBuilder.Options);
    }
}
