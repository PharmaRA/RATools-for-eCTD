using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Ctd;
using RATools.Application.Finalization;
using RATools.Application.RegionalMetadata;
using RATools.Application.Standards;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Ctd;
using RATools.Domain.Documents;
using RATools.Domain.RegionalMetadata;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Tests.Persistence.Postgres;

namespace RATools.Tests.RegionalMetadata;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresRegionalMetadataTests
{
    [RequiresPostgresFact]
    public async Task OrderedEnvelopesInheritanceAndIndependentPlacementContextsSurviveNewConnections()
    {
        await using var db = Context(); var rig = await SeedAsync(db); var service = Service(db);
        var first = await service.SaveSequenceAsync(rig.App.Id, "0001", rig.Revision, rig.SnapshotId, Content(),
            [new(rig.Node.Id, "de", "de", "spc", null)],
            [new(rig.First.Id, rig.Node.Id, "fr", "fr", "pl", null), new(rig.Second.Id, rig.Node.Id, "common", "en", "combined", null)]);
        var defaults = await service.SaveApplicationAsync(rig.App.Id, 0, rig.SnapshotId,
            new(1, "EU", Eu: new("Inherited applicant", [Text("PT-2"), Text("PT-1")], [Text("Name B"), Text("Name A")], [])));
        var inherited = await service.InheritAsync(rig.App.Id, "0001", first.WorkspaceRevision, defaults.Id);
        await service.SaveApplicationAsync(rig.App.Id, defaults.MetadataRevision, rig.SnapshotId,
            new(1, "EU", Eu: new("Changed later", [], [Text("Changed later")], [])));
        await using var read = Context(); var restored = (await new EfCoreRegionalMetadataStore(read).GetRevisionAsync(inherited.Id))!;
        Assert.Equal(JsonSerializer.Serialize(inherited), JsonSerializer.Serialize(restored));
        Assert.Equal(new[] { "PT-2", "PT-1" }, restored.Content!.Eu!.Envelopes[0].ProcedureTrackingNumbers.Select(item => item.Value));
        Assert.All(restored.Content.Eu.Envelopes, envelope => Assert.Equal("Inherited applicant", envelope.Applicant));
        Assert.Equal(new[] { "fr", "common" }, restored.DocumentContexts.Select(item => item.Country));
        Assert.Equal(rig.First.DocumentId, rig.Second.DocumentId); Assert.NotEqual(rig.First.Id, rig.Second.Id);
        Assert.Equal(defaults.Id, restored.InheritedDefaultsId); Assert.Equal(defaults.Digest, restored.InheritedDefaultsDigest);
        Assert.Equal(rig.Revision + 2, await new EfCoreWorkspaceRevisionStore(read).GetRevisionAsync(rig.App.Id, "0001"));
        Assert.False(restored.IsReady);
    }

    [RequiresPostgresFact]
    public async Task IndependentConnectionsAcceptOneExpectedWorkspaceRevisionAndRejectTheOther()
    {
        await using var first = Context(); var rig = await SeedAsync(first); await using var second = Context();
        async Task<bool> Write(RAToolsDbContext database)
        {
            try { await Service(database).SaveSequenceAsync(rig.App.Id, "0001", rig.Revision, rig.SnapshotId, Content()); return true; }
            catch (WorkspaceRevisionConflictException) { return false; }
            catch (RegionalMetadataConflictException) { return false; }
        }
        var results = await Task.WhenAll(Write(first), Write(second)); Assert.Single(results, value => value);
        await using var read = Context();
        Assert.Equal(1, await read.Set<RegionalMetadataRecord>().CountAsync(row => row.ApplicationId == rig.App.Id));
        Assert.Equal(rig.Revision + 1, await new EfCoreWorkspaceRevisionStore(read).GetRevisionAsync(rig.App.Id, "0001"));
    }

    [RequiresPostgresFact]
    public async Task SameRevisionIdentityRacingOnTwoConnectionsReturnsTheExactArchiveOnce()
    {
        await using var first = Context(); var rig = await SeedAsync(first); await using var second = Context();
        var revision = RegionalMetadataRevision.Create(Guid.NewGuid(), rig.App.Id, null, 1, null, rig.SnapshotId, null,
            new(1, "EU", Eu: new("Exact defaults", [], [Text("Original")], [])), [], [], null, null, [], [], null, null, DateTime.UtcNow);
        Task<RegionalMetadataRevision> Append(RAToolsDbContext database) => new EfCorePersistenceTransaction(database)
            .ExecuteAsync(ct => new EfCoreRegionalMetadataStore(database).AppendAsync(revision, null, ct));
        var results = await Task.WhenAll(Append(first), Append(second));
        Assert.Equal(JsonSerializer.Serialize(results[0]), JsonSerializer.Serialize(results[1]));
        Assert.Equal(revision.Id, results[0].Id);
        Assert.Equal(1, await first.Set<RegionalMetadataRecord>().CountAsync(row => row.ApplicationId == rig.App.Id));
    }

    [RequiresPostgresFact]
    public async Task FailureAfterArchiveAndHeadWritesRollsBackEveryProjectionAndWorkspaceRevision()
    {
        await using var db = Context(); var rig = await SeedAsync(db); var service = Service(db, new FailAfterAppend(new EfCoreRegionalMetadataStore(db)));
        await Assert.ThrowsAsync<IOException>(() => service.SaveSequenceAsync(rig.App.Id, "0001", rig.Revision, rig.SnapshotId, Content(),
            [new(rig.Node.Id, "de", "de", "spc", null)], [new(rig.First.Id, rig.Node.Id, "fr", "fr", "pl", null)]));
        await using var read = Context();
        Assert.False(await read.Set<RegionalMetadataRecord>().AnyAsync(row => row.ApplicationId == rig.App.Id));
        Assert.False(await read.Set<RegionalMetadataHeadRecord>().AnyAsync(row => row.ApplicationId == rig.App.Id));
        Assert.False(await read.Set<RegionalNodeContextRecord>().AnyAsync(row => row.ApplicationId == rig.App.Id));
        Assert.False(await read.Set<RegionalDocumentContextRecord>().AnyAsync(row => row.ApplicationId == rig.App.Id));
        Assert.Equal(rig.Revision, await new EfCoreWorkspaceRevisionStore(read).GetRevisionAsync(rig.App.Id, "0001"));
        Assert.Equal(2, await read.DocumentPlacements.CountAsync(row => row.ApplicationId == rig.App.Id));
    }

    [RequiresPostgresFact]
    public async Task CompositeForeignKeysAndAppendOnlyDatabaseGuardsRefuseContradictoryRows()
    {
        await using var db = Context(); var rig = await SeedAsync(db);
        var saved = await Service(db).SaveSequenceAsync(rig.App.Id, "0001", rig.Revision, rig.SnapshotId, Content());
        var foreign = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO regional_metadata_node_contexts ("MetadataId", "ApplicationId", "SequenceNumber", "NodeInstanceId", "Ordinal", "Data")
            VALUES ({saved.Id}, {rig.App.Id}, '0000', {rig.Node.Id}, 0, {"{}"})
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, foreign.SqlState);
        var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE regional_metadata_revisions SET \"Data\" = '{{}}' WHERE \"Id\" = {saved.Id}"));
        Assert.Equal("55000", update.SqlState);
        var definitions = await db.Database.SqlQueryRaw<string>("""
            SELECT pg_get_triggerdef(oid)::text AS "Value" FROM pg_trigger
            WHERE tgrelid = 'regional_metadata_revisions'::regclass AND NOT tgisinternal
            """).ToArrayAsync();
        Assert.Contains(definitions, definition => definition.Contains("DELETE", StringComparison.Ordinal));
        Assert.Contains(definitions, definition => definition.Contains("TRUNCATE", StringComparison.Ordinal));
        Assert.Equal(saved.Digest, (await new EfCoreRegionalMetadataStore(db).GetRevisionAsync(saved.Id))!.Digest);
    }

    [RequiresPostgresFact]
    public async Task LegacyUpgradeReplaysItsExactRawEvidenceAndDoesNotMaterializeEuWriterDefaults()
    {
        await using var db = Context(); var rig = await SeedAsync(db); var service = Service(db);
        var before = await db.Sequences.AsNoTracking().SingleAsync(row => row.ApplicationId == rig.App.Id && row.SequenceNumber == "0001");
        var first = await service.UpgradeLegacyAsync(rig.App.Id, "0001", rig.Revision);
        var again = await service.UpgradeLegacyAsync(rig.App.Id, "0001", rig.Revision);
        Assert.Equal(first.Id, again.Id); Assert.Equal(first.LegacyJson, again.LegacyJson); Assert.Equal(first.Digest, again.Digest);
        Assert.Empty(first.Content!.Eu!.Envelopes); Assert.Contains(first.Diagnostics, item => item.Code == "LegacyDefaultsUnconfirmed");
        Assert.DoesNotContain(first.Provenance, item => item.Status == "Confirmed");
        await using var read = Context();
        var after = await read.Sequences.AsNoTracking().SingleAsync(row => row.ApplicationId == rig.App.Id && row.SequenceNumber == "0001");
        Assert.Equal(before.FdaApplicantName, after.FdaApplicantName); Assert.Equal(before.FdaEmail, after.FdaEmail);
        Assert.Equal(rig.Revision + 1, after.WorkspaceRevision);
    }

    [RequiresPostgresFact]
    public async Task NonNullInheritanceCannotBypassApplicationDefaultsForeignKeyWithNullScope()
    {
        await using var db = Context(); var rig = await SeedAsync(db);
        var invalid = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO regional_metadata_revisions ("Id", "ApplicationId", "ScopeKey", "SequenceNumber", "MetadataRevision", "WorkspaceRevision",
                "StandardsSnapshotId", "InheritedDefaultsId", "InheritedDefaultsScope", "Data", "Digest", "DataSha256", "CreatedUtc")
            VALUES ({Guid.NewGuid()}, {rig.App.Id}, '0001', '0001', 1, {rig.Revision + 1}, {rig.SnapshotId}, {Guid.NewGuid()}, NULL,
                {"{}"}, 'invalid-inheritance', 'invalid-inheritance', {DateTime.UtcNow})
            """));
        Assert.Equal(PostgresErrorCodes.CheckViolation, invalid.SqlState);
        Assert.False(await db.Set<RegionalMetadataRecord>().AnyAsync(row => row.ApplicationId == rig.App.Id));
    }

    [RequiresPostgresFact]
    public async Task HeadCannotRewindAndDocumentContextMustMatchTheExactPlacementNode()
    {
        await using var db = Context(); var rig = await SeedAsync(db); var service = Service(db);
        var first = await service.SaveSequenceAsync(rig.App.Id, "0001", rig.Revision, rig.SnapshotId, Content());
        var second = await service.SaveSequenceAsync(rig.App.Id, "0001", first.WorkspaceRevision, rig.SnapshotId, Content());
        var rewind = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE regional_metadata_heads SET "MetadataId" = {first.Id} WHERE "ApplicationId" = {rig.App.Id} AND "ScopeKey" = '0001'
            """));
        Assert.Equal("55000", rewind.SqlState);
        var nodeMismatch = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO regional_metadata_document_contexts ("MetadataId", "PlacementId", "ApplicationId", "SequenceNumber", "NodeInstanceId", "Ordinal", "Data")
            VALUES ({second.Id}, {rig.First.Id}, {rig.App.Id}, '0001', NULL, 0, {"{}"})
            """));
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, nodeMismatch.SqlState);
        Assert.Equal(second.Id, (await new EfCoreRegionalMetadataStore(db).GetHeadAsync(rig.App.Id, "0001"))!.Id);
    }

    [RequiresPostgresFact]
    public async Task RegionalMetadataMigrationAndRuntimeModelAreCurrent()
    {
        await using var db = Context(); Assert.Empty(await db.Database.GetPendingMigrationsAsync()); Assert.False(db.Database.HasPendingModelChanges());
    }

    private static RAToolsDbContext Context() => new PostgresFixture().CreateDbContext();
    private static RegionalMetadataService Service(RAToolsDbContext db, IRegionalMetadataStore? store = null)
    {
        var applications = new EfCoreApplicationRepository(db); var revisions = new EfCoreWorkspaceRevisionStore(db); var transaction = new EfCorePersistenceTransaction(db);
        var snapshots = new StandardsSnapshotService(new EfCoreStandardsSnapshotStore(db), new EuEctd322StandardsProfileProvider());
        return new(store ?? new EfCoreRegionalMetadataStore(db), applications, new EfCoreCtdNodeRepository(db), new EfCoreDocumentPlacementRepository(db), snapshots,
            new WorkspaceMutationCoordinator(revisions, transaction, finalizedGuard: new FinalizedWorkspaceGuard(new EfCoreFinalizationStore(db))), revisions, transaction);
    }
    private static async Task<Rig> SeedAsync(RAToolsDbContext db)
    {
        var root = Path.GetFullPath(".artifacts/p603-postgres/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        var app = new SubmissionApplication("p603-" + Guid.NewGuid().ToString("N"), "EU", "Synthetic", root, "eu-ectd-3.2.2");
        app.CreateSequence("0000", "initial", "Earlier"); app.CreateSequence("0001", "initial", "Metadata")
            .RevisePublishingMetadata(SequencePublishingMetadata.Create(null, "initial", null, "Stored shared description", "Stored shared applicant", null, email: "legacy@example.test"));
        await new EfCoreApplicationRepository(db).AddAsync(app);
        var graph = new CtdNodeGraph(app.Id, IchSectionDefinitions.Current);
        var node = graph.Create("m1-administrative-information-and-prescribing-information", null, new Dictionary<string, string>());
        var nodes = new EfCoreCtdNodeRepository(db);
        foreach (var sequence in new[] { "0000", "0001" }) await nodes.SaveSequenceAsync(graph, sequence, [new(graph, sequence, node.Id, node.IdentityAttributes)], 0);
        var path = Path.Combine(root, "source.txt"); var bytes = Encoding.UTF8.GetBytes("Retained regional context source"); await File.WriteAllBytesAsync(path, bytes);
        var document = new SubmissionDocument("source.txt", "text/plain", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), path);
        await new EfCoreDocumentRepository(db).AddAsync(document);
        DocumentPlacement Placement(string leaf) { var value = new DocumentPlacement(document.Id, app.Id, "0001", "m1", DocumentPlacementOperation.New, leaf); value.BindToNode(new(graph, "0001", node.Id, node.IdentityAttributes), 0); return value; }
        var first = Placement("first"); var second = Placement("second"); var placements = new EfCoreDocumentPlacementRepository(db); await placements.AddAsync(first); await placements.AddAsync(second);
        var snapshot = await new StandardsSnapshotService(new EfCoreStandardsSnapshotStore(db), new EuEctd322StandardsProfileProvider()).CaptureAsync("eu-ectd-3.2.2");
        return new(app, node, first, second, snapshot.Id, (await new EfCoreWorkspaceRevisionStore(db).GetRevisionAsync(app.Id, "0001"))!.Value);
    }
    private static RegionalTextEntry Text(string value) => new(Guid.NewGuid(), value);
    private static RegionalMetadataContent Content() => new(1, "EU", Eu: new([
        new(Guid.NewGuid(), "identifier-de", "de", "DE-BFARM", "national", "maa", "single", null, "initial", "Applicant DE", [Text("DE-2"), Text("DE-1")], [Text("Name B"), Text("Name A")], [], [new(Guid.NewGuid(), "0001")], "Description DE"),
        new(Guid.NewGuid(), "identifier-fr", "fr", "FR-ANSM", "national", "maa", "single", null, "initial", "Applicant FR", [Text("FR-1")], [Text("French Name")], [Text("INN")], [new(Guid.NewGuid(), "0001")], "Description FR") ]));
    private sealed record Rig(SubmissionApplication App, CtdNodeInstance Node, DocumentPlacement First, DocumentPlacement Second, string SnapshotId, long Revision);
    private sealed class FailAfterAppend(IRegionalMetadataStore inner) : IRegionalMetadataStore
    {
        public Task<RegionalMetadataRevision?> GetHeadAsync(Guid applicationId, string? sequenceNumber, CancellationToken cancellationToken = default) => inner.GetHeadAsync(applicationId, sequenceNumber, cancellationToken);
        public Task<RegionalMetadataRevision?> GetRevisionAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetRevisionAsync(id, cancellationToken);
        public Task<RegionalMetadataRevision?> FindLegacyAsync(Guid applicationId, string sequenceNumber, string digest, CancellationToken cancellationToken = default) => inner.FindLegacyAsync(applicationId, sequenceNumber, digest, cancellationToken);
        public async Task<RegionalMetadataRevision> AppendAsync(RegionalMetadataRevision revision, Guid? expectedHeadId, CancellationToken cancellationToken = default)
        { await inner.AppendAsync(revision, expectedHeadId, cancellationToken); throw new IOException("Injected failure after regional archive/head/context writes."); }
    }
}
