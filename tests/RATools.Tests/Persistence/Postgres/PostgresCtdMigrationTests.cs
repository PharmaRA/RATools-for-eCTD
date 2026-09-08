using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresCtdMigrationTests
{
    private const string PreviousMigration = "20260905143131_PreserveImportedLeafIds";

    [RequiresPostgresFact]
    public async Task IndependentMigratorUpgradesAnEmptyDatabaseTwiceAndEmptySchemaCanBeRolledBack()
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        var first = await database.RunMigratorAsync();
        Assert.Contains("Applied", first);
        Assert.Contains("no migrations were applied", await database.RunMigratorAsync());
        await using var context = database.CreateContext();
        Assert.Equal(160, await context.CtdDefinitions.CountAsync());
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        await context.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        Assert.Contains("Applied", await database.RunMigratorAsync());
        Assert.Equal(160, await context.CtdDefinitions.CountAsync());
    }

    [RequiresPostgresFact]
    public async Task LegacyDatabasePreviewUpgradeBackfillAndRepeatPreserveActualDocumentBytes()
    {
        await using var database = await TemporaryDatabase.CreateAsync();
        await using (var oldSchema = database.CreateContext())
            await oldSchema.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        var applicationId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var placementId = Guid.NewGuid();
        var directory = Path.Combine(Path.GetTempPath(), $"ratools-ctd-upgrade-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "specification.pdf");
        var source = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Publisher", "sequences", "0000", "m1", "us", "12-cover-letters", "cover.pdf");
        File.Copy(source, path);
        var originalBytes = await File.ReadAllBytesAsync(path);
        var sha256 = Convert.ToHexString(SHA256.HashData(originalBytes)).ToLowerInvariant();
        var md5 = Convert.ToHexString(MD5.HashData(originalBytes)).ToLowerInvariant();
        try
        {
            await using (var connection = new NpgsqlConnection(database.ConnectionString))
            {
                await connection.OpenAsync();
                await using var command = new NpgsqlCommand("""
                    INSERT INTO applications ("Id", "ApplicationNumber", "Region", "SponsorName", "EctdTemplateKey", "WorkingDirectoryPath", "CreatedUtc")
                    VALUES (@app, @number, 'US', 'Synthetic', 'us-fda-ectd-3.2.2', @directory, NOW());
                    INSERT INTO sequences ("ApplicationId", "SequenceNumber", "SubmissionType", "Description", "CreatedUtc")
                    VALUES (@app, '0000', 'original', 'legacy migration test', NOW());
                    INSERT INTO documents ("Id", "FileName", "MediaType", "FileSize", "Sha256", "Md5", "StoragePath", "CreatedUtc")
                    VALUES (@doc, 'specification.pdf', 'application/pdf', @length, @sha, @md5, @path, NOW());
                    INSERT INTO document_placements ("Id", "ApplicationId", "SequenceNumber", "DocumentId", "CtdSection", "Operation", "Title", "LeafId", "CreatedUtc")
                    VALUES (@placement, @app, '0000', @doc, 'm3.2.s.4.1', 'new', 'legacy specification', 'ExternalLeafOriginal', NOW());
                    """, connection);
                command.Parameters.AddWithValue("app", applicationId);
                command.Parameters.AddWithValue("number", $"legacy-{applicationId:N}");
                command.Parameters.AddWithValue("directory", directory);
                command.Parameters.AddWithValue("doc", documentId);
                command.Parameters.AddWithValue("placement", placementId);
                command.Parameters.AddWithValue("length", (long)originalBytes.Length);
                command.Parameters.AddWithValue("sha", sha256);
                command.Parameters.AddWithValue("md5", md5);
                command.Parameters.AddWithValue("path", path);
                await command.ExecuteNonQueryAsync();
            }
            Assert.Contains("Applied 1", await database.RunMigratorAsync());
            await using var upgraded = database.CreateContext();
            Assert.Null((await upgraded.DocumentPlacements.AsNoTracking().SingleAsync()).NodeInstanceId);
            Assert.Equal(0, (await upgraded.Sequences.AsNoTracking().SingleAsync()).WorkspaceRevision);
            Assert.Contains("\"Preview\":true", await database.RunMigratorAsync("--preview-ctd-backfill"));
            Assert.Equal(0, await upgraded.CtdNodeInstances.CountAsync());
            Assert.Contains("\"BoundPlacements\":1", await database.RunMigratorAsync("--backfill-ctd"));
            var placement = await upgraded.DocumentPlacements.AsNoTracking().SingleAsync();
            var ids = await upgraded.CtdNodeInstances.AsNoTracking().Select(row => row.Id).ToArrayAsync();
            Assert.NotNull(placement.NodeInstanceId);
            Assert.Equal("ExternalLeafOriginal", placement.LeafId);
            Assert.Equal(documentId, placement.DocumentId);
            Assert.Equal("LegacyNodeIdentityUnresolved", (await upgraded.NodeBackfillDiagnostics.SingleAsync()).Code);
            Assert.Contains("\"Changed\":false", await database.RunMigratorAsync("--backfill-ctd"));
            Assert.Equal(ids.Order(), (await upgraded.CtdNodeInstances.Select(row => row.Id).ToArrayAsync()).Order());
            Assert.Equal(1, (await upgraded.Sequences.AsNoTracking().SingleAsync()).WorkspaceRevision);
            var document = await upgraded.Documents.AsNoTracking().SingleAsync();
            Assert.Equal(path, document.StoragePath);
            Assert.Equal(sha256, document.Sha256);
            Assert.Equal(md5, document.Md5);
            Assert.Equal(originalBytes, await File.ReadAllBytesAsync(path));
            var error = await Assert.ThrowsAsync<PostgresException>(() => upgraded.GetService<IMigrator>().MigrateAsync(PreviousMigration));
            Assert.Equal("55000", error.SqlState);
            Assert.Equal(ids.Length, await upgraded.CtdNodeInstances.CountAsync());
            Assert.Empty(await upgraded.Database.GetPendingMigrationsAsync());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class TemporaryDatabase(string connectionString, string adminConnectionString, string name) : IAsyncDisposable
    {
        public string ConnectionString { get; } = connectionString;

        public static async Task<TemporaryDatabase> CreateAsync()
        {
            var builder = new NpgsqlConnectionStringBuilder(PostgresTestEnvironment.ConnectionString) { Pooling = false, Database = "postgres" };
            var admin = builder.ConnectionString;
            var name = $"ratools_ctd_test_{Guid.NewGuid():N}";
            await using var connection = new NpgsqlConnection(admin);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", connection);
            await command.ExecuteNonQueryAsync();
            builder.Database = name;
            return new TemporaryDatabase(builder.ConnectionString, admin, name);
        }

        public RAToolsDbContext CreateContext() => new(new DbContextOptionsBuilder<RAToolsDbContext>().UseNpgsql(ConnectionString).Options);

        public async Task<string> RunMigratorAsync(params string[] arguments)
        {
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (root is not null && !File.Exists(Path.Combine(root.FullName, "Directory.Build.props"))) root = root.Parent;
            Assert.NotNull(root);
            var configuration = typeof(PostgresCtdMigrationTests).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()!.Configuration;
            var program = Path.Combine(root.FullName, "src", "RATools.DatabaseMigrator", "bin", configuration, "net8.0", "RATools.DatabaseMigrator.dll");
            Assert.True(File.Exists(program), "Build the independent migrator before running database qualification.");
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add(program);
            foreach (var argument in arguments) start.ArgumentList.Add(argument);
            start.Environment["ConnectionStrings__PostgreSql"] = ConnectionString;
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
            Assert.True(process.ExitCode == 0, $"Migrator exited with {process.ExitCode}: {(await error).Replace(ConnectionString, "<connection>", StringComparison.Ordinal)}");
            return await output;
        }

        public async ValueTask DisposeAsync()
        {
            await using var connection = new NpgsqlConnection(adminConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"DROP DATABASE \"{name}\" WITH (FORCE)", connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}
