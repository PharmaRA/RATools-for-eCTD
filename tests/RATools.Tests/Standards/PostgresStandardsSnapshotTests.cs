using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Application.Standards;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Tests.Persistence.Postgres;

namespace RATools.Tests.Standards;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresStandardsSnapshotTests
{
    [RequiresPostgresFact]
    public async Task TwoConnectionsArchiveExactlyOneIdentityAndReplayTheFirstAcquisition()
    {
        await using var first = Context(); await using var second = Context();
        var one = await CaptureAsync(first);
        var later = StandardsProfileSnapshot.Create(one.Descriptor, one.Resources, one.AcquiredUtc.AddHours(1));
        var stored = await Task.WhenAll(new EfCoreStandardsSnapshotStore(first).AddAsync(one),
            new EfCoreStandardsSnapshotStore(second).AddAsync(later));
        Assert.Equal(one.Id, stored[0].Id); Assert.Equal(one.Id, stored[1].Id);
        Assert.Equal(stored[0].AcquiredUtc, stored[1].AcquiredUtc);
        Assert.Equal(1, await second.Set<StandardsSnapshotRecord>().CountAsync(row => row.Id == one.Id));
        var restored = (await new EfCoreStandardsSnapshotStore(second).GetAsync(one.Id))!;
        Assert.Equal("4.7", restored.Descriptor.CriteriaVersion);
        var criteria = Assert.Single(restored.Resources, resource => resource.Key == "official-fda-criteria");
        Assert.Equal("2b390199bebb1b7cde65926d027120ae576ca304f2628950cc51d2c9e4a3ab8c", criteria.Sha256);
        Assert.Equal("Verified", Assert.Single(restored.Descriptor.Evidence, evidence => evidence.Category == "Sources").Status);
        Assert.Equal(JsonSerializer.Serialize(stored[0]), JsonSerializer.Serialize(restored));
        foreach (var resource in one.Resources)
            Assert.Equal(resource.ReadBytes(), restored.Resources.Single(item => item.Key == resource.Key).ReadBytes());
        await OfficialSourceSnapshotTests.RetainCaptureEvidenceAsync(restored, "PostgreSQL US dual connection immutable source replay");
    }

    [RequiresPostgresFact]
    public async Task ArchiveRejectsUpdatesAndItsDatabaseGuardsCoverDeletionAndTruncation()
    {
        await using var database = Context(); var snapshot = await CaptureAsync(database);
        await new EfCoreStandardsSnapshotStore(database).AddAsync(snapshot);
        var error = await Assert.ThrowsAsync<PostgresException>(() => database.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE standards_profile_snapshots SET \"TemplateKey\" = 'changed' WHERE \"Id\" = {snapshot.Id}"));
        Assert.Equal("55000", error.SqlState);
        var unchanged = (await new EfCoreStandardsSnapshotStore(database).GetAsync(snapshot.Id))!;
        Assert.Equal(snapshot.TemplateKey, unchanged.TemplateKey);
        // Read trigger definitions to verify the forbidden deletion paths without
        // attempting any deletion or truncation in this retained test database.
        var definitions = await database.Database.SqlQueryRaw<string>("""
            SELECT pg_get_triggerdef(oid)::text AS "Value" FROM pg_trigger
            WHERE tgrelid = 'standards_profile_snapshots'::regclass AND NOT tgisinternal
            """).ToArrayAsync();
        Assert.Contains(definitions, definition => definition.Contains("DELETE", StringComparison.Ordinal) && definition.Contains("UPDATE", StringComparison.Ordinal));
        Assert.Contains(definitions, definition => definition.Contains("TRUNCATE", StringComparison.Ordinal));
    }

    [RequiresPostgresFact]
    public async Task AnInsertedPayloadWithMismatchedDatabaseIdentityIsNeverReturned()
    {
        await using var database = Context(); var snapshot = await CaptureAsync(database);
        var data = JsonSerializer.Serialize(snapshot);
        var id = "sha256:" + Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        database.Add(new StandardsSnapshotRecord
        {
            Id = id, SchemaVersion = snapshot.SchemaVersion, TemplateKey = snapshot.TemplateKey,
            RuleProfileAlias = snapshot.RuleProfileAlias, AcquiredUtc = snapshot.AcquiredUtc,
            Data = data, DataSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(data))).ToLowerInvariant()
        });
        await database.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => new EfCoreStandardsSnapshotStore(database).GetAsync(id));
    }

    [RequiresPostgresFact]
    public async Task SnapshotMigrationIsAppliedAndRuntimeModelMatchesItsSnapshot()
    {
        await using var database = Context();
        Assert.Empty(await database.Database.GetPendingMigrationsAsync());
        Assert.False(database.Database.HasPendingModelChanges());
    }

    private static RAToolsDbContext Context() => new PostgresFixture().CreateDbContext();
    private static async Task<StandardsProfileSnapshot> CaptureAsync(RAToolsDbContext database)
    {
        var service = new StandardsSnapshotService(new EfCoreStandardsSnapshotStore(database), new FdaEctd322StandardsProfileProvider());
        var source = await service.CaptureAsync("us-fda-ectd-3.2.2");
        // Isolate each case's immutable identity without modifying shared evidence.
        var descriptor = source.Descriptor with { DisplayName = source.Descriptor.DisplayName + " test " + Guid.NewGuid().ToString("N") };
        return StandardsProfileSnapshot.Create(descriptor, source.Resources, source.AcquiredUtc);
    }
}
