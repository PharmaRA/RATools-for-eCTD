using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Assembly;
using RATools.Application.Ctd;
using RATools.Application.DocumentProcessing;
using RATools.Application.Documents;
using RATools.Application.Finalization;
using RATools.Application.Workspaces;
using RATools.Domain.Documents;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Tests.Persistence.Postgres;

namespace RATools.Tests.Assembly;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresAssemblyTests
{
    [RequiresPostgresFact]
    public Task IntakeRegistrationFailureRollsBackTheRealDatabase() => ExerciseIntakeFailureAsync(false);

    [RequiresPostgresFact]
    public Task IntakePromotionFailureRollsBackTheRealDatabase() => ExerciseIntakeFailureAsync(true);

    private static async Task ExerciseIntakeFailureAsync(bool promotionFailure)
    {
        await using var db = Context(); var rig = await SeedAsync(db);
        var service = Build(rig.Data, db, new EfCoreDocumentPlacementRepository(db),
            new FailIntake(new EfCoreDocumentRepository(db), promotionFailure));

        await Assert.ThrowsAsync<IOException>(() => service.UploadSourceAsync(rig.Data.Application.Id, "0001", rig.Data.Revision,
            "source.pdf", new MemoryStream(rig.Data.Original)));

        await using var read = Context();
        Assert.Equal(1, await read.Documents.CountAsync(row => row.StoragePath.StartsWith(rig.Data.Root)));
        Assert.Equal(rig.Data.Revision, await new EfCoreWorkspaceRevisionStore(read).GetRevisionAsync(rig.Data.Application.Id, "0001"));
        Assert.Empty(await rig.Service.ListSourcesAsync(rig.Data.Application.Id, "0001"));
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(rig.Data.Root, "0001", "_assembly-failed"), "*", SearchOption.AllDirectories));
    }

    [RequiresPostgresFact]
    public async Task IntakeCommitResponseLossPreservesTheRegisteredFileInANewContext()
    {
        await using var db = Context(); var rig = await SeedAsync(db);
        var service = Build(rig.Data, db, new EfCoreDocumentPlacementRepository(db),
            transactions: new LostResponseTransaction(new EfCorePersistenceTransaction(db)));

        var receipt = await service.UploadSourceAsync(rig.Data.Application.Id, "0001", rig.Data.Revision, "source.pdf", new MemoryStream(rig.Data.Original));

        await using var read = Context();
        var loaded = (await new EfCoreDocumentRepository(read).GetAsync(receipt.DocumentId))!;
        Assert.Equal(rig.Data.Original, await File.ReadAllBytesAsync(loaded.StoragePath));
        Assert.Equal(rig.Data.Revision, await new EfCoreWorkspaceRevisionStore(read).GetRevisionAsync(rig.Data.Application.Id, "0001"));
    }

    private sealed class FailIntake(IDocumentRepository inner, bool promotionFailure) : IDocumentRepository
    {
        public async Task AddAsync(SubmissionDocument document, CancellationToken cancellationToken = default)
        {
            await inner.AddAsync(document, cancellationToken);
            if (promotionFailure) Directory.CreateDirectory(Path.GetDirectoryName(document.StoragePath)!);
            else throw new IOException("Injected registration failure after SaveChanges.");
        }
        public Task<bool> UpdateAsync(SubmissionDocument document, CancellationToken cancellationToken = default) => inner.UpdateAsync(document, cancellationToken);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No deletion.");
        public Task<SubmissionDocument?> GetAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);
        public Task<IReadOnlyCollection<SubmissionDocument>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
    }

    private sealed class LostResponseTransaction(IPersistenceTransaction inner) : IPersistenceTransaction
    {
        public async Task ExecuteAsync(Func<CancellationToken, Task> operation, CancellationToken cancellationToken = default)
        {
            await inner.ExecuteAsync(operation, cancellationToken);
            throw new IOException("Injected lost commit response.");
        }
        public async Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken = default)
        {
            await inner.ExecuteAsync(operation, cancellationToken);
            throw new IOException("Injected lost commit response.");
        }
    }

    [RequiresPostgresFact]
    public async Task TwoConnectionsReplayOneRequestAndTwoDistinctConstraintRowsReturnConflict()
    {
        await using var db = Context(); var rig = await SeedAsync(db);
        var first = await rig.Service.PreviewAsync(rig.Data.Application.Id, "0001", new(rig.Data.Revision, rig.Data.Manifest(1)));
        var second = await rig.Service.PreviewAsync(rig.Data.Application.Id, "0001", new(rig.Data.Revision, rig.Data.Manifest(2)));
        var key = "pg-assembly-" + Guid.NewGuid().ToString("N");
        await using var other = Context(); var a = new EfCoreAssemblyStore(db); var b = new EfCoreAssemblyStore(other);
        var enqueued = await Task.WhenAll(a.EnqueueAsync(AssemblyEvidence.Request(first, key)), b.EnqueueAsync(AssemblyEvidence.Request(first, key)));
        try
        {
            Assert.Single(enqueued, item => item.Created); Assert.Equal(enqueued[0].Job.State.Id, enqueued[1].Job.State.Id);
            var secondKey = "pg-assembly-" + Guid.NewGuid().ToString("N");
            var next = await a.EnqueueAsync(AssemblyEvidence.Request(second, secondKey));
            try
            {
                await Assert.ThrowsAsync<AssemblyPlanInvalidException>(() => b.EnqueueAsync(AssemblyEvidence.Request(first, secondKey)));
                Assert.Equal(first.ManifestSha256, (await b.GetPlanAsync(first.Id))!.ManifestSha256);
                Assert.Equal(first.Sources[0].Sha256, (await b.GetPlanAsync(first.Id))!.Sources[0].Sha256);
            }
            finally { await a.CancelAsync(next.Job.State.Id, DateTime.UtcNow); }
        }
        finally { await a.CancelAsync(enqueued[0].Job.State.Id, DateTime.UtcNow); }
    }

    [RequiresPostgresFact]
    public async Task IndependentClaimsFenceExpiredLeaseAndCancellationStopsRecovery()
    {
        await using var db = Context(); var rig = await SeedAsync(db);
        var plan = await rig.Service.PreviewAsync(rig.Data.Application.Id, "0001", new(rig.Data.Revision, rig.Data.Manifest(1)));
        var queued = await rig.Service.EnqueueAsync(rig.Data.Application.Id, "0001", new(plan.Id, plan.Plan.PlanDigest, Guid.NewGuid().ToString("N")));
        var a = new EfCoreAssemblyStore(db); await using var other = Context(); var b = new EfCoreAssemblyStore(other); var now = DateTime.UtcNow;
        await OnlyOwnedCandidateAsync(queued.Job.State.Id, now);
        var claims = await Task.WhenAll(a.ClaimAsync("pg-a", now, TimeSpan.FromSeconds(1)), b.ClaimAsync("pg-b", now, TimeSpan.FromSeconds(1)));
        var first = Assert.Single(claims, item => item is not null)!;
        var second = (await b.ClaimAsync("pg-recover", now.AddSeconds(2), TimeSpan.FromSeconds(1)))!;
        Assert.Equal(first.Job.Id, second.Job.Id); Assert.Equal(2, second.Job.AttemptCount);
        Assert.False(await a.HeartbeatAsync(first.Job.Id, first.Token, first.Owner, now.AddSeconds(2), TimeSpan.FromSeconds(1)));
        await a.CancelAsync(second.Job.Id, now.AddSeconds(2));
        Assert.Null(await b.ClaimAsync("pg-cancelled", now.AddSeconds(4), TimeSpan.FromSeconds(1)));
        Assert.Equal(PdfProcessingJobStatus.Cancelled, (await a.GetJobAsync(second.Job.Id))!.State.Status);
        Assert.Empty(await new EfCoreDocumentPlacementRepository(other).ListBySequenceAsync(rig.Data.Application.Id, "0001"));
    }

    [RequiresPostgresFact]
    public async Task RealAssemblyCommitsTwoNewDocumentsPlacementsRevisionAndAuditTogether()
    {
        await ExerciseCommitAsync(failSecond: false);
    }

    [RequiresPostgresFact]
    public async Task SecondRowFailureRollsBackAllDatabaseMutationsAndRetainsPreparationEvidence()
    {
        await ExerciseCommitAsync(failSecond: true);
    }

    [RequiresPostgresFact]
    public async Task CorruptRequestIsQuarantinedWithoutChangingEvidenceAndNextJobCanProgress()
    {
        await using var db = Context(); var rig = await SeedAsync(db); var store = new EfCoreAssemblyStore(db);
        var first = await rig.Service.PreviewAsync(rig.Data.Application.Id, "0001", new(rig.Data.Revision, rig.Data.Manifest(1)));
        var second = await rig.Service.PreviewAsync(rig.Data.Application.Id, "0001", new(rig.Data.Revision, rig.Data.Manifest(2)));
        var corrupt = (await store.EnqueueAsync(AssemblyEvidence.Request(first, Guid.NewGuid().ToString("N")))).Job;
        var next = (await store.EnqueueAsync(AssemblyEvidence.Request(second, Guid.NewGuid().ToString("N")))).Job;
        var evidence = await db.Set<AssemblyExecutionRecord>().AsNoTracking().SingleAsync(row => row.Id == corrupt.State.Id);
        await db.Set<AssemblyExecutionRecord>().Where(row => row.Id == corrupt.State.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Digest, new string('0', 64)));
        var now = DateTime.UtcNow;
        Assert.False(await db.Set<AssemblyExecutionRecord>().AnyAsync(row => row.Id != corrupt.State.Id && row.Id != next.State.Id &&
            (row.Status == PdfProcessingJobStatus.Pending || row.Status == PdfProcessingJobStatus.Running && row.LeaseExpiresUtc <= now)), "Only test-owned candidates may run.");
        var lease = (await store.ClaimAsync("pg-quarantine", now, TimeSpan.FromMinutes(1)))!;
        try
        {
            Assert.Equal(next.State.Id, lease.Job.Id);
            var retained = await db.Set<AssemblyExecutionRecord>().AsNoTracking().SingleAsync(row => row.Id == corrupt.State.Id);
            Assert.Equal(evidence.Data, retained.Data); Assert.Equal(new string('0', 64), retained.Digest); Assert.NotNull(retained.QuarantinedUtc);
            Assert.Equal(PdfProcessingJobStatus.Failed, retained.Status);
            await AssertImmutableAsync(() => db.Set<AssemblyPlanRecord>().Where(row => row.Id == first.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Data, "changed")));
            await AssertImmutableAsync(() => db.Set<AssemblyExecutionRecord>().Where(row => row.Id == corrupt.State.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, PdfProcessingJobStatus.Pending)));
        }
        finally { await store.FailAsync(next.State.Id, lease.Token, lease.Owner, DateTime.UtcNow, PdfProcessingJobStatus.Cancelled, "TEST_COMPLETE", "Retained qualification evidence."); }
    }

    [RequiresPostgresFact]
    public async Task SourceIntakeIsScopedRecoverableAndDoesNotAdvanceTheStoredRevision()
    {
        await using var db = Context(); var rig = await SeedAsync(db);
        var receipt = await rig.Service.UploadSourceAsync(rig.Data.Application.Id, "0001", rig.Data.Revision, "intake.pdf", new MemoryStream(rig.Data.Original));
        await using var read = Context();
        Assert.Equal(rig.Data.Revision, await new EfCoreWorkspaceRevisionStore(read).GetRevisionAsync(rig.Data.Application.Id, "0001"));
        var loaded = (await new EfCoreDocumentRepository(read).GetAsync(receipt.DocumentId))!;
        Assert.Equal(rig.Data.Document.Sha256, loaded.Sha256); Assert.Equal(rig.Data.Original, await File.ReadAllBytesAsync(loaded.StoragePath));
        Assert.Single(await rig.Service.ListSourcesAsync(rig.Data.Application.Id, "0001"));
        Assert.Empty(await new EfCoreDocumentPlacementRepository(read).ListBySequenceAsync(rig.Data.Application.Id, "0001"));
    }

    [RequiresPostgresFact]
    public async Task MigrationIsCurrentAndRuntimeModelMatchesTheSnapshot()
    {
        await using var db = Context(); Assert.Empty(await db.Database.GetPendingMigrationsAsync()); Assert.False(db.Database.HasPendingModelChanges());
    }

    private static async Task ExerciseCommitAsync(bool failSecond)
    {
        await using var db = Context(); var rig = await SeedAsync(db); var normal = new EfCoreDocumentPlacementRepository(db);
        var service = Build(rig.Data, db, failSecond ? new FailSecondPlacement(normal) : normal);
        var plan = await service.PreviewAsync(rig.Data.Application.Id, "0001", new(rig.Data.Revision, rig.Data.Manifest(2)));
        var request = new AssemblyEnqueueRequest(plan.Id, plan.Plan.PlanDigest, Guid.NewGuid().ToString("N"));
        var queued = await service.EnqueueAsync(rig.Data.Application.Id, "0001", request); var store = new EfCoreAssemblyStore(db); var now = DateTime.UtcNow;
        await OnlyOwnedCandidateAsync(queued.Job.State.Id, now);
        var lease = (await store.ClaimAsync("pg-commit", now, TimeSpan.FromMinutes(2)))!;
        if (failSecond)
        {
            await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(lease));
            Assert.True(await store.FailAsync(lease.Job.Id, lease.Token, lease.Owner, DateTime.UtcNow, PdfProcessingJobStatus.Failed, "TEST_ROW_FAILURE", "Retained preparation evidence."));
        }
        else await service.ExecuteAsync(lease);
        await using var read = Context();
        var job = (await new EfCoreAssemblyStore(read).GetJobAsync(lease.Job.Id))!;
        var mounted = await new EfCoreDocumentPlacementRepository(read).ListBySequenceAsync(rig.Data.Application.Id, "0001");
        var revision = await new EfCoreWorkspaceRevisionStore(read).GetRevisionAsync(rig.Data.Application.Id, "0001");
        Assert.Equal(failSecond ? 0 : 2, mounted.Count); Assert.Equal(rig.Data.Revision + (failSecond ? 0 : 1), revision);
        Assert.Equal(failSecond ? 1 : 3, await read.Documents.CountAsync(row => row.StoragePath.StartsWith(rig.Data.Root)));
        if (failSecond) Assert.Null(job.Result);
        else
        {
            Assert.Equal(PdfProcessingJobStatus.Completed, job.State.Status); Assert.Equal(2, job.Result!.Documents.Count);
            Assert.Equal(lease.Job.Id, (await service.EnqueueAsync(rig.Data.Application.Id, "0001", request)).Job.State.Id);
            foreach (var output in job.Result.Documents)
            {
                var document = (await new EfCoreDocumentRepository(read).GetAsync(output.DocumentId))!;
                Assert.NotEqual(rig.Data.Document.Id, document.Id); Assert.Equal(rig.Data.Document.Md5, document.Md5);
                Assert.Equal(rig.Data.Original, await File.ReadAllBytesAsync(document.StoragePath));
            }
        }
        Assert.Equal(rig.Data.Original, await File.ReadAllBytesAsync(rig.Data.Document.StoragePath));
    }
    private static RAToolsDbContext Context() => new PostgresFixture().CreateDbContext();
    private static async Task<(AssemblyRig Data, AssemblyService Service)> SeedAsync(RAToolsDbContext db)
    {
        var data = await AssemblyRig.CreateAsync();
        await new EfCoreApplicationRepository(db).AddAsync(data.Application);
        var workspace = (await data.Nodes.GetSequenceAsync(data.Application.Id, "0001"))!;
        var revision = await new EfCoreCtdNodeRepository(db).SaveSequenceAsync(workspace.Graph, "0001", workspace.Nodes, 0);
        Assert.Equal(data.Revision, revision); await new EfCoreDocumentRepository(db).AddAsync(data.Document);
        return (data, Build(data, db, new EfCoreDocumentPlacementRepository(db)));
    }
    private static AssemblyService Build(AssemblyRig data, RAToolsDbContext db, IDocumentPlacementRepository placements,
        IDocumentRepository? intakeDocuments = null, IPersistenceTransaction? transactions = null)
    {
        var applications = new EfCoreApplicationRepository(db); var documents = new EfCoreDocumentRepository(db); var nodes = new EfCoreCtdNodeRepository(db);
        var revisions = new EfCoreWorkspaceRevisionStore(db); var boundary = new DocumentStorageBoundary(new AssemblyRig.Paths(data.Root)); var options = new AssemblyExecutionOptions();
        var mutations = new WorkspaceMutationCoordinator(revisions, transactions ?? new EfCorePersistenceTransaction(db), finalizedGuard: new FinalizedWorkspaceGuard(new EfCoreFinalizationStore(db)));
        return new(new AssemblyPlanner(applications, documents, placements, nodes, boundary, revisions), new EfCoreAssemblyStore(db), applications,
            intakeDocuments ?? documents, placements, nodes, new EfCoreDocumentProcessingStore(db), boundary,
            new CtdNodePathResolver(new RATools.Application.Validation.EctdWorkspacePathResolver()), new AssemblyFileStore(new AssemblyRig.Paths(data.Root), boundary, options),
            mutations, new PdfPigPackageInspector(), options);
    }
    private static async Task OnlyOwnedCandidateAsync(Guid id, DateTime now)
    {
        await using var db = Context(); Assert.False(await db.Set<AssemblyExecutionRecord>().AnyAsync(row => row.Id != id &&
            (row.Status == PdfProcessingJobStatus.Pending || row.Status == PdfProcessingJobStatus.Running && row.LeaseExpiresUtc <= now)), "Use the dedicated serial PostgreSQL instance without live workers.");
    }
    private static async Task AssertImmutableAsync(Func<Task> write)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(write);
        while (error is not PostgresException && error.InnerException is not null) error = error.InnerException;
        Assert.Equal("55000", Assert.IsType<PostgresException>(error).SqlState);
    }
    private sealed class FailSecondPlacement(IDocumentPlacementRepository inner) : IDocumentPlacementRepository
    {
        private int _count;
        public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => ++_count == 2 ? Task.FromException(new IOException("Injected second row failure.")) : inner.AddAsync(placement, cancellationToken);
        public Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => inner.UpdateAsync(placement, cancellationToken);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No deletion in this qualification.");
        public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid app, CancellationToken cancellationToken = default) => inner.ListByApplicationAsync(app, cancellationToken);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid app, string seq, CancellationToken cancellationToken = default) => inner.ListBySequenceAsync(app, seq, cancellationToken);
    }
}
