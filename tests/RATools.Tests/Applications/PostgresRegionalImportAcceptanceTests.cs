using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using RATools.Application.Applications;
using RATools.Application.Applications.Requests;
using RATools.Application.Standards;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Tests.Persistence.Postgres;

namespace RATools.Tests.Applications;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresRegionalImportAcceptanceTests
{
    private static readonly string[] ExpectedValues1 = new[] { "First product", "Second product" };
    private static readonly string[] ExpectedValues2 = new[] { "de", "fr" };

    [RequiresPostgresFact]
    public async Task DatabaseRejectsMismatchedSnapshotsAndDigestsAndKeepsEvidenceImmutable()
    {
        var fixture = await ImportAcceptanceFixture.CreateAsync();
        await using var db = Context(); var result = await Importer(db, fixture).ImportAsync(Request(fixture));
        var source = await db.Set<ImportedRegionalEvidenceRecord>().AsNoTracking().FirstAsync(row => row.ApplicationId == result.ApplicationId);
        var other = await new StandardsSnapshotService(new EfCoreStandardsSnapshotStore(db), new FdaEctd322StandardsProfileProvider()).CaptureAsync("us-fda-ectd-3.2.2");
        async Task Insert(string snapshot, string digest, string path)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO regional_import_evidence ("Id","ApplicationId","SequenceNumber","MetadataId","StandardsSnapshotId","RelativePath","OriginalBytes","Sha256")
                VALUES ({Guid.NewGuid()},{source.ApplicationId},{source.SequenceNumber},{source.MetadataId},{snapshot},{path},{source.OriginalBytes},{digest})
                """);
        }
        var wrongSnapshot = await Assert.ThrowsAsync<PostgresException>(() => Insert(other.Id, source.Sha256, "rejected-snapshot.xml"));
        Assert.Equal("23503", wrongSnapshot.SqlState);
        var wrongDigest = await Assert.ThrowsAsync<PostgresException>(() => Insert(source.StandardsSnapshotId, new string('0',64), "rejected-digest.xml"));
        Assert.Equal("23514", wrongDigest.SqlState);
        var immutable = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE regional_import_evidence SET \"RelativePath\"={"changed.xml"} WHERE \"Id\"={source.Id}"));
        Assert.Equal("55000", immutable.SqlState);
        Assert.Equal(2, await db.Set<ImportedRegionalEvidenceRecord>().CountAsync(row => row.ApplicationId == result.ApplicationId));
        Assert.Equal(source.Sha256, (await db.Set<ImportedRegionalEvidenceRecord>().AsNoTracking().SingleAsync(row => row.Id == source.Id)).Sha256);
        var guards = await db.Database.SqlQueryRaw<int>("SELECT count(*)::integer AS \"Value\" FROM pg_trigger WHERE tgrelid='regional_import_evidence'::regclass AND tgname IN ('regional_import_evidence_immutable','regional_import_evidence_no_truncate','regional_import_evidence_scope') AND NOT tgisinternal").SingleAsync();
        Assert.Equal(3, guards);
    }

    [RequiresPostgresFact]
    public async Task ImportPersistsExactEvidenceHeadAndPlacementContextsForANewConnection()
    {
        var fixture = await ImportAcceptanceFixture.CreateAsync();
        await using var db = Context(); var result = await Importer(db, fixture).ImportAsync(Request(fixture));
        Assert.Equal(1, result.ImportedSequenceCount);
        await using var read = Context(); var head = (await new EfCoreRegionalMetadataStore(read).GetHeadAsync(result.ApplicationId, "0000"))!;
        Assert.NotNull(head); Assert.Equal(1, head.WorkspaceRevision); Assert.False(head.IsReady);
        var application = (await new EfCoreApplicationRepository(read).GetAsync(result.ApplicationId))!;
        Assert.Equal(1, application.Sequences.Single().WorkspaceRevision);
        Assert.Equal(ExpectedValues1, head.Content!.Eu!.Envelopes[0].InventedNames.Select(item => item.Value));
        Assert.Equal(ExpectedValues2, head.DocumentContexts.Select(item => item.Language));
        var imports = Store(read);
        ImportAcceptanceFixture.CheckEvidence(await imports.GetRegionalEvidenceAsync(result.ApplicationId, "0000"), head.Id, head.StandardsSnapshotId, fixture.RegionalBytes);
        Assert.Equal(2, (await imports.GetBackbonesAsync(result.ApplicationId, "0000")).Count);
        var placements = await read.DocumentPlacements.Where(row => row.ApplicationId == result.ApplicationId).ToArrayAsync();
        Assert.Equal(2, placements.Length); Assert.Single(placements.Select(row => row.DocumentId).Distinct());
        Assert.All(head.DocumentContexts, context => Assert.Contains(placements, row => row.Id == context.PlacementId && row.NodeInstanceId == context.NodeInstanceId));
    }

    [RequiresPostgresFact]
    public async Task FailureAfterRealEvidenceInsertRollsBackEveryImportedRecordAndProjection()
    {
        var fixture = await ImportAcceptanceFixture.CreateAsync(); var fault = new FailAfterEvidenceSave();
        await using var db = Context(fault);
        await Assert.ThrowsAsync<IOException>(() => Importer(db, fixture).ImportAsync(Request(fixture)));
        Assert.True(fault.Fired); Assert.NotEqual(Guid.Empty, fault.ApplicationId);
        await using var read = Context(); var id = fault.ApplicationId;
        Assert.False(await read.Applications.AnyAsync(row => row.Id == id));
        Assert.False(await read.Sequences.AnyAsync(row => row.ApplicationId == id));
        Assert.False(await read.CtdNodeInstances.AnyAsync(row => row.ApplicationId == id));
        Assert.False(await read.SequenceNodes.AnyAsync(row => row.ApplicationId == id));
        Assert.False(await read.DocumentPlacements.AnyAsync(row => row.ApplicationId == id));
        Assert.False(await read.Documents.AnyAsync(row => fault.DocumentIds.Contains(row.Id)));
        Assert.False(await read.ImportedBackbones.AnyAsync(row => row.ApplicationId == id));
        Assert.False(await read.Set<RegionalMetadataRecord>().AnyAsync(row => row.ApplicationId == id));
        Assert.False(await read.Set<RegionalMetadataHeadRecord>().AnyAsync(row => row.ApplicationId == id));
        Assert.False(await read.Set<ImportedRegionalEvidenceRecord>().AnyAsync(row => row.ApplicationId == id));
        Assert.False(await read.NodeBackfillDiagnostics.AnyAsync(row => row.ApplicationId == id));
    }

    private static RAToolsDbContext Context(IInterceptor? fault = null)
    {
        var builder = new DbContextOptionsBuilder<RAToolsDbContext>().UseNpgsql(Environment.GetEnvironmentVariable("RATOOLS_TEST_POSTGRES") ?? throw new InvalidOperationException("Dedicated PostgreSQL required."));
        if (fault is not null) builder.AddInterceptors(fault);
        return new(builder.Options);
    }
    private static EfCoreApplicationImportStore Store(RAToolsDbContext db) => new(db, new EfCoreApplicationRepository(db), new EfCoreDocumentRepository(db), new EfCoreDocumentPlacementRepository(db), new EfCoreRegionalMetadataStore(db));
    private static ApplicationImportService Importer(RAToolsDbContext db, ImportAcceptanceFixture fixture) => new(new EfCoreApplicationRepository(db), Store(db), fixture,
        new StandardsSnapshotService(new EfCoreStandardsSnapshotStore(db), new CompositeStandardsProfileProvider([new FdaEctd322StandardsProfileProvider(), new EuEctd322StandardsProfileProvider()])));
    private static ImportApplicationRequest Request(ImportAcceptanceFixture fixture) => new(fixture.Root, "eu-ectd-3.2.2", "Request sponsor");
    private sealed class FailAfterEvidenceSave : SaveChangesInterceptor
    {
        internal bool Fired { get; private set; }
        internal Guid ApplicationId { get; private set; }
        internal Guid[] DocumentIds { get; private set; } = [];
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            var db = eventData.Context!; var source = db.ChangeTracker.Entries<ImportedRegionalEvidenceRecord>().FirstOrDefault();
            if (source is not null)
            {
                Fired = true; ApplicationId = source.Entity.ApplicationId; DocumentIds = db.ChangeTracker.Entries<DocumentRecord>().Select(entry => entry.Entity.Id).ToArray();
                throw new IOException("Injected after actual raw-evidence insert inside import transaction.");
            }
            return new(result);
        }
    }
}
