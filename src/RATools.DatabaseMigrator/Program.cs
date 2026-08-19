using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Security;

var configuration = new ConfigurationManager();
configuration.AddEnvironmentVariables();
FileSecretConfiguration.Apply(configuration);

var connectionString = configuration.GetConnectionString("PostgreSql");
if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("ConnectionStrings:PostgreSql is required.");
}

var options = new DbContextOptionsBuilder<RAToolsDbContext>()
    .UseNpgsql(connectionString)
    .Options;

await using var dbContext = new RAToolsDbContext(options);
var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync()).ToArray();
if (pendingMigrations.Length == 0)
{
    Console.WriteLine("Database schema is current; no migrations were applied.");
    return;
}

await dbContext.Database.MigrateAsync();
Console.WriteLine($"Applied {pendingMigrations.Length} database migration(s); schema is current.");
