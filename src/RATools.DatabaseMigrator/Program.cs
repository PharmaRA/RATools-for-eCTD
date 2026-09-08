using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Security;

if (args.Any(argument => argument is not ("--backfill-ctd" or "--preview-ctd-backfill")) || args.Length > 1)
    throw new ArgumentException("Use no arguments for schema migration, --preview-ctd-backfill, or --backfill-ctd.");

using var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };

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
var pendingMigrations = (await dbContext.Database.GetPendingMigrationsAsync(cancellation.Token)).ToArray();
if (args.FirstOrDefault() == "--preview-ctd-backfill" && pendingMigrations.Length > 0)
    throw new InvalidOperationException("Apply schema migrations before requesting a read-only node backfill preview.");
if (pendingMigrations.Length == 0)
{
    Console.WriteLine("Database schema is current; no migrations were applied.");
}
else
{
    await dbContext.Database.MigrateAsync(cancellation.Token);
    Console.WriteLine($"Applied {pendingMigrations.Length} database migration(s); schema is current.");
}

if (args.Length == 1)
{
    var sequences = await dbContext.Sequences.AsNoTracking().OrderBy(row => row.ApplicationId).ThenBy(row => row.SequenceNumber)
        .Select(row => new { row.ApplicationId, row.SequenceNumber }).ToArrayAsync(cancellation.Token);
    var backfill = new EfCoreNodeBackfill(dbContext);
    foreach (var sequence in sequences)
    {
        var result = await backfill.RunAsync(sequence.ApplicationId, sequence.SequenceNumber, args[0] == "--preview-ctd-backfill", cancellation.Token);
        Console.WriteLine(JsonSerializer.Serialize(result));
        dbContext.ChangeTracker.Clear();
    }
}
