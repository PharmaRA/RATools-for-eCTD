using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications;
using RATools.Application.Applications.Requests;
using RATools.Application.RegionalMetadata;
using RATools.Application.Standards;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.RegionalMetadata;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Tests.Persistence.Postgres;

namespace RATools.Tests.RegionalMetadata;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresRegionalMetadataEditingTests
{
    [RequiresPostgresFact]
    public async Task MixedSharedAndRegionalSaveCommitsOneRevisionAndSurvivesAnIndependentRead()
    {
        await using var db = Context(); var (app, snapshot) = await Seed(db);
        var saved = await Service(db).UpdateAsync(app.Id, "0001", Request(snapshot));
        Assert.Equal(1, saved!.WorkspaceRevision); Assert.Equal(1, saved.Regional!.WorkspaceRevision);
        await using var read = Context(); var restored = (await Service(read).GetAsync(app.Id, "0001"))!;
        Assert.Equal("Atomic shared description", restored.SequenceDescription); Assert.Equal("Atomic shared applicant", restored.ApplicantName);
        Assert.Equal("Ordered applicant", restored.Regional!.Content!.Eu!.Envelopes[0].Applicant);
        Assert.Equal(1, await read.Set<RegionalMetadataRecord>().CountAsync(row => row.ApplicationId == app.Id));
        Assert.Equal(saved.Regional.Digest, restored.Regional.Digest);
        await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => Service(read).UpdateAsync(app.Id, "0001", Request(snapshot)));
    }

    [RequiresPostgresFact]
    public async Task InjectedPostAppendFailureRollsBackSharedDataHeadAndWorkspaceOnRealPostgres()
    {
        await using var db = Context(); var (app, snapshot) = await Seed(db);
        await Assert.ThrowsAsync<IOException>(() => Service(db, true).UpdateAsync(app.Id, "0001", Request(snapshot)));
        await using var read = Context(); var restored = (await Service(read).GetAsync(app.Id, "0001"))!;
        Assert.Equal("Original sequence description", restored.SequenceDescription); Assert.Equal("Original sponsor", restored.ApplicantName);
        Assert.Equal(0, restored.WorkspaceRevision); Assert.Null(restored.Regional);
        Assert.False(await read.Set<RegionalMetadataRecord>().AnyAsync(row => row.ApplicationId == app.Id));
        Assert.False(await read.Set<RegionalMetadataHeadRecord>().AnyAsync(row => row.ApplicationId == app.Id));
    }
    private static RAToolsDbContext Context() => new PostgresFixture().CreateDbContext();
    private static async Task<(SubmissionApplication App, string Snapshot)> Seed(RAToolsDbContext db)
    {
        var app = new SubmissionApplication("p604-" + Guid.NewGuid().ToString("N"), "EU", "Original sponsor", Path.GetFullPath(".artifacts/p604-postgres/" + Guid.NewGuid().ToString("N")), "eu-ectd-3.2.2");
        app.CreateSequence("0001", "initial", "Original sequence description"); await new EfCoreApplicationRepository(db).AddAsync(app);
        var snapshot = await new StandardsSnapshotService(new EfCoreStandardsSnapshotStore(db), new EuEctd322StandardsProfileProvider()).CaptureAsync(app.EctdTemplateKey);
        return (app, snapshot.Id);
    }
    private static SequencePublishingMetadataService Service(RAToolsDbContext db, bool fail = false)
    {
        var apps = new EfCoreApplicationRepository(db); var revisions = new EfCoreWorkspaceRevisionStore(db); var transaction = new EfCorePersistenceTransaction(db);
        var mutations = new WorkspaceMutationCoordinator(revisions, transaction); var profiles = new EuEctd322StandardsProfileProvider();
        var snapshots = new StandardsSnapshotService(new EfCoreStandardsSnapshotStore(db), profiles);
        IRegionalMetadataStore store = new EfCoreRegionalMetadataStore(db); if (fail) store = new FailingStore(store);
        var regional = new RegionalMetadataService(store, apps, new EfCoreCtdNodeRepository(db), new EfCoreDocumentPlacementRepository(db), snapshots, mutations, revisions, transaction);
        return new(apps, profiles, mutations, regional);
    }
    private static UpdateSequencePublishingMetadataRequest Request(string snapshot) => new(null, "initial", null, "Atomic shared description", "Atomic shared applicant", null, null, null, null, null, null, 0,
        new(snapshot, new(1, "EU", Eu: new([new(Guid.NewGuid(), "identifier", "de", "DE-BFARM", "national", "maa", null, null, "initial", "Ordered applicant", [], [], [], [new(Guid.NewGuid(), "0001")], "Description")]))));
    private sealed class FailingStore(IRegionalMetadataStore inner) : IRegionalMetadataStore
    {
        public Task<RegionalMetadataRevision?> GetHeadAsync(Guid applicationId, string? sequenceNumber, CancellationToken cancellationToken = default) => inner.GetHeadAsync(applicationId, sequenceNumber, cancellationToken);
        public Task<RegionalMetadataRevision?> GetRevisionAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetRevisionAsync(id, cancellationToken);
        public Task<RegionalMetadataRevision?> FindLegacyAsync(Guid applicationId, string sequenceNumber, string legacyDigest, CancellationToken cancellationToken = default) => inner.FindLegacyAsync(applicationId, sequenceNumber, legacyDigest, cancellationToken);
        public async Task<RegionalMetadataRevision> AppendAsync(RegionalMetadataRevision revision, Guid? expectedHeadId, CancellationToken cancellationToken = default)
        { await inner.AppendAsync(revision, expectedHeadId, cancellationToken); throw new IOException("Injected after real PostgreSQL append."); }
    }
}
