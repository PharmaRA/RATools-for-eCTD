using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.DocumentProcessing;
using RATools.Application.Documents;
using RATools.Application.Finalization;
using RATools.Application.Publishing;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Infrastructure.DocumentProcessing;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Tests.Persistence.Postgres;

namespace RATools.Tests.DocumentProcessing;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresProcessingAdoptionTests
{
    [RequiresPostgresFact]
    public async Task RealAdoptionCommitsContentReferenceRevisionAndAuditTogether()
    {
        await ExerciseAsync(rejectAdvance: false);
    }

    [RequiresPostgresFact]
    public async Task FailedRevisionCommitLeavesNoAdoptedDocumentOrPlacementChange()
    {
        await ExerciseAsync(rejectAdvance: true);
    }

    private static async Task ExerciseAsync(bool rejectAdvance)
    {
        await using var db = new PostgresFixture().CreateDbContext();
        var root = Path.GetFullPath($".artifacts/p5-06-pg-workflow/{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "0001", "m3"));
        var application = new SubmissionApplication("processing-adopt-" + Guid.NewGuid().ToString("N"), "US", "Synthetic", root, "us-fda-ectd-3.2.2");
        application.CreateSequence("0001", "original", "PDF processing transaction test");
        var applications = new EfCoreApplicationRepository(db);
        var documents = new EfCoreDocumentRepository(db);
        var placements = new EfCoreDocumentPlacementRepository(db);
        await applications.AddAsync(application);
        var bytes = await File.ReadAllBytesAsync("tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
        var sourcePath = Path.Combine(root, "0001", "m3", "source.pdf");
        var targetPath = Path.Combine(root, "0001", "m3", "old.pdf");
        await File.WriteAllBytesAsync(sourcePath, bytes);
        await File.WriteAllBytesAsync(targetPath, bytes);
        var source = new SubmissionDocument("source.pdf", "application/pdf", bytes.Length, sha, md5, sourcePath);
        var target = new SubmissionDocument("old.pdf", "application/pdf", bytes.Length, sha, md5, targetPath);
        await documents.AddAsync(source); await documents.AddAsync(target);
        var placement = new DocumentPlacement(source.Id, application.Id, "0001", "m3.2.s.4.1", DocumentPlacementOperation.New, "Source");
        await placements.AddAsync(placement);
        await placements.AddAsync(new(target.Id, application.Id, "0001", "m3.2.s.4.1", DocumentPlacementOperation.New, "Target"));
        var revisions = new ConditionalRevisionStore(new EfCoreWorkspaceRevisionStore(db), rejectAdvance);
        var mutations = new WorkspaceMutationCoordinator(revisions, new EfCorePersistenceTransaction(db),
            finalizedGuard: new FinalizedWorkspaceGuard(new EfCoreFinalizationStore(db)));
        var store = new EfCoreDocumentProcessingStore(db);
        var paths = new TestPaths(root);
        var options = new PdfProcessingExecutionOptions();
        var files = new ProcessingFileStore(paths, options);
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions
        { ExecutablePath = Environment.GetEnvironmentVariable("RATOOLS_QPDF") ?? Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe") });
        var service = new DocumentProcessingService(store, applications, documents, placements, mutations,
            new DocumentStorageBoundary(paths), files, engine, new PdfPigPackageInspector(), options);
        var inspection = await service.InspectAsync(application.Id, "0001", placement.Id, 0);
        var plan = await service.CreatePlanAsync(application.Id, "0001", placement.Id, new(inspection.SourceVersionId, 0, new("UseOutlines", null, null)));
        var queued = await service.EnqueueAsync(application.Id, "0001", plan.Id, plan.PlanDigest, Guid.NewGuid().ToString("N"));
        var now = DateTime.UtcNow;
        Assert.False(await db.Set<PdfProcessingJobRecord>().AnyAsync(row => row.Id != queued.Job.Id &&
            (row.Status == PdfProcessingJobStatus.Pending || (row.Status == PdfProcessingJobStatus.Running && row.LeaseExpiresUtc <= now))),
            "Processing adoption tests require their dedicated serial database without application workers.");
        var lease = (await store.ClaimAsync("pg-workflow", now, TimeSpan.FromMinutes(2)))!;
        await service.ExecuteAsync(lease);
        Assert.Equal(PdfProcessingJobStatus.Completed, (await store.GetJobAsync(queued.Job.Id))!.Status);
        if (rejectAdvance)
            await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => service.AdoptAsync(application.Id, "0001", queued.Job.Id, 0));
        else
            await service.AdoptAsync(application.Id, "0001", queued.Job.Id, 0);

        // Independent connection proves committed database state, not EF tracking.
        await using var read = new PostgresFixture().CreateDbContext();
        var adopted = await new EfCoreDocumentProcessingStore(read).GetAdoptionAsync(queued.Job.Id);
        var loadedPlacement = (await new EfCoreDocumentPlacementRepository(read).GetAsync(placement.Id))!;
        var revision = await new EfCoreWorkspaceRevisionStore(read).GetRevisionAsync(application.Id, "0001");
        if (rejectAdvance)
        {
            Assert.Null(adopted);
            Assert.Equal(source.Id, loadedPlacement.DocumentId);
            Assert.Equal(0, revision);
            Assert.Equal(2, await read.Documents.CountAsync(row => row.StoragePath.StartsWith(root)));
        }
        else
        {
            Assert.NotNull(adopted);
            Assert.Equal(adopted.DocumentId, loadedPlacement.DocumentId);
            Assert.Equal(placement.LeafId, loadedPlacement.LeafId);
            Assert.Equal(1, revision);
            var document = (await new EfCoreDocumentRepository(read).GetAsync(adopted.DocumentId))!;
            Assert.Equal("m3/source.pdf", PublishOutputNaming.BuildPublishedDocumentRelativePath(document, "0001"));
            Assert.NotEqual(source.Id, document.Id);
            Assert.NotEqual(source.Sha256, document.Sha256);
            Assert.Equal(adopted.DocumentId, (await service.AdoptAsync(application.Id, "0001", queued.Job.Id, 0)).DocumentId);
        }
        Assert.Equal(bytes, await File.ReadAllBytesAsync(sourcePath));
    }

    private sealed class ConditionalRevisionStore(IWorkspaceRevisionStore inner, bool reject) : IWorkspaceRevisionStore
    {
        public Task<IAsyncDisposable> LockApplicationAsync(Guid id, CancellationToken cancellationToken = default) => inner.LockApplicationAsync(id, cancellationToken);
        public Task<long?> GetRevisionAsync(Guid id, string sequence, CancellationToken cancellationToken = default) => inner.GetRevisionAsync(id, sequence, cancellationToken);
        public Task<bool> AdvanceAsync(Guid id, string sequence, long revision, CancellationToken cancellationToken = default) =>
            reject ? Task.FromResult(false) : inner.AdvanceAsync(id, sequence, revision, cancellationToken);
    }

    private sealed class TestPaths(string root) : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [root];
        public string EnsureAllowed(string path)
        {
            var full = Path.GetFullPath(path);
            if (full != root && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("Outside test workspace.");
            return full;
        }
    }
}
