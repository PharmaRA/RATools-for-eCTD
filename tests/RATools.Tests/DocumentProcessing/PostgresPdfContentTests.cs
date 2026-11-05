using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.DocumentProcessing;
using RATools.Application.Documents;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Infrastructure.DocumentProcessing;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.DocumentProcessing;

public sealed class PostgresPdfContentTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContentEvidenceRoundTripsAndAdoptionCommitsOrRollsBackAtomically(bool rejectRevision)
    {
        await using var db = Context();
        Assert.False(await db.Set<PdfProcessingJobRecord>().AnyAsync(row => row.Status == PdfProcessingJobStatus.Pending || row.Status == PdfProcessingJobStatus.Running),
            "Use the dedicated test instance without unrelated pending/running PDF jobs.");
        Assert.False(db.Database.HasPendingModelChanges());
        var root = Path.GetFullPath(".artifacts/p508-pg/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "0001", "m3"));
        var path = Path.Combine(root, "0001", "m3", "source.pdf"); await PdfContentTestRig.CreateFixtureAsync("version", path);
        var application = new SubmissionApplication("p508pg-" + Guid.NewGuid().ToString("N"), "US", "Synthetic", root, "us-fda-ectd-3.2.2");
        application.CreateSequence("0001", "original", "Content PG");
        var applications = new EfCoreApplicationRepository(db); await applications.AddAsync(application);
        var documents = new EfCoreDocumentRepository(db); var placements = new EfCoreDocumentPlacementRepository(db);
        var bytes = await File.ReadAllBytesAsync(path);
        var document = new SubmissionDocument("source.pdf", "application/pdf", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), path); await documents.AddAsync(document);
        var placement = new DocumentPlacement(document.Id, application.Id, "0001", "m3.2.s.4.1", DocumentPlacementOperation.New, "Content PG source");
        await placements.AddAsync(placement);
        var store = new EfCoreDocumentProcessingStore(db);
        var revisions = new ConditionalRevision(new EfCoreWorkspaceRevisionStore(db), rejectRevision);
        var paths = new PdfContentTestRig.Paths(root); var processing = new PdfProcessingExecutionOptions();
        using var content = new PythonPdfContentEngine(PdfContentTestRig.OptionsFor(root), new() { ExecutablePath = PdfContentTestRig.Qpdf });
        var service = new DocumentProcessingService(store, applications, documents, placements,
            new WorkspaceMutationCoordinator(revisions, new EfCorePersistenceTransaction(db)), new DocumentStorageBoundary(paths),
            new ProcessingFileStore(paths, processing), new QpdfProcessingEngine(new() { ExecutablePath = PdfContentTestRig.Qpdf }),
            new PdfPigPackageInspector(), processing, content);
        var inspection = await service.InspectAsync(application.Id, "0001", placement.Id, 0);
        var plan = await service.CreatePlanAsync(application.Id, "0001", placement.Id,
            new(inspection.SourceVersionId, 0, new(null, null, null, Content: new(null, false, "1.7"))));
        var queued = await service.EnqueueAsync(application.Id, "0001", plan.Id, plan.PlanDigest, "contentpg-" + plan.Id);
        try
        {
            var lease = (await store.ClaimAsync("contentpg", DateTime.UtcNow, TimeSpan.FromMinutes(3)))!;
            Assert.Equal(queued.Job.Id, lease.Job.Id); await service.ExecuteAsync(lease);
            var completed = (await store.GetJobAsync(lease.Job.Id))!;
            await using var read = Context(); var reader = new EfCoreDocumentProcessingStore(read);
            var version = (await reader.GetVersionAsync(completed.ResultVersionId!.Value))!;
            Assert.Contains("receiptSha256", version.EvidenceJson, StringComparison.Ordinal);
            Assert.Contains("peakMemoryBytes", version.EvidenceJson, StringComparison.Ordinal);
            var evidence = await service.GetContentReceiptAsync(application.Id, "0001", lease.Job.Id);
            Assert.Contains(evidence.ReceiptSha256, version.EvidenceJson, StringComparison.Ordinal);
            if (rejectRevision)
                await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => service.AdoptAsync(application.Id, "0001", lease.Job.Id, 0));
            else
            {
                var adopted = await service.AdoptAsync(application.Id, "0001", lease.Job.Id, 0);
                Assert.Equal(1, adopted.WorkspaceRevision);
                Assert.Equal(adopted.DocumentId, (await service.AdoptAsync(application.Id, "0001", lease.Job.Id, 0)).DocumentId);
            }
            Assert.Equal(rejectRevision ? 0 : 1, await new EfCoreWorkspaceRevisionStore(read).GetRevisionAsync(application.Id, "0001"));
            Assert.Equal(rejectRevision, (await new EfCoreDocumentPlacementRepository(read).GetAsync(placement.Id))!.DocumentId == document.Id);
            Assert.Equal(rejectRevision, await reader.GetAdoptionAsync(lease.Job.Id) is null);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            var state = await store.CancelAsync(queued.Job.Id, DateTime.UtcNow);
            if (state?.Status == PdfProcessingJobStatus.Running)
                await store.FailAsync(state.Id, state.LeaseToken!.Value, state.LeaseOwner!, DateTime.UtcNow, PdfProcessingJobStatus.Cancelled,
                    "TEST_FINISHED", "Task-owned test evidence retained.");
        }
    }

    private static RAToolsDbContext Context() => new(new DbContextOptionsBuilder<RAToolsDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("RATOOLS_TEST_POSTGRES") ?? throw new InvalidOperationException("RATOOLS_TEST_POSTGRES is required.")).Options);
    private sealed class ConditionalRevision(IWorkspaceRevisionStore inner, bool reject) : IWorkspaceRevisionStore
    {
        public Task<IAsyncDisposable> LockApplicationAsync(Guid id, CancellationToken cancellationToken = default) => inner.LockApplicationAsync(id, cancellationToken);
        public Task<long?> GetRevisionAsync(Guid id, string sequence, CancellationToken cancellationToken = default) => inner.GetRevisionAsync(id, sequence, cancellationToken);
        public Task<bool> AdvanceAsync(Guid id, string sequence, long revision, CancellationToken cancellationToken = default) => reject ? Task.FromResult(false) : inner.AdvanceAsync(id, sequence, revision, cancellationToken);
    }
}
