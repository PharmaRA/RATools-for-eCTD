using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.DocumentProcessing;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Infrastructure.DocumentProcessing;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.DocumentProcessing;

public sealed class PostgresPdfRenameTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptQueueRowIsQuarantinedWithoutChangingItsEvidenceOrStarvingValidWork(bool validJson)
    {
        await using var db = Context(); await EnsureNoForeignCandidatesAsync(db);
        var bad = await SeedAsync(db); var good = await SeedAsync(db);
        var corruptId = Guid.NewGuid(); var raw = validJson ? "{\"State\":null,\"Results\":[]}" : "malformed retained evidence";
        var digest = PdfRenameStore.Hash(raw);
        db.Add(new PdfRenameJobRecord { Id = corruptId, PlanId = bad.Preview.Id, IdempotencyKey = "corrupt-" + corruptId,
            CreatedUtc = DateTime.UtcNow.AddMinutes(-1), Status = PdfProcessingJobStatus.Pending, Data = raw, Digest = digest });
        await db.SaveChangesAsync();
        var queued = await good.Store.EnqueueAsync(PdfRenameRequests.Create(good.Preview, "after-corrupt-" + good.Preview.Id));
        try
        {
            var lease = (await good.Store.ClaimAsync("integrity", DateTime.UtcNow, TimeSpan.FromMinutes(1)))!;
            Assert.NotNull(lease); Assert.Equal(queued.Job.State.Id, lease.Job.Id);
            var retained = await db.Set<PdfRenameJobRecord>().AsNoTracking().SingleAsync(row => row.Id == corruptId);
            Assert.Equal(raw, retained.Data); Assert.Equal(digest, retained.Digest); Assert.NotNull(retained.QuarantinedUtc);
            Assert.Equal(PdfProcessingJobStatus.Failed, retained.Status);
            Assert.Equal("REQUEST_INTEGRITY_FAILED", (await good.Store.GetJobAsync(corruptId))!.State.FailureCode);
        }
        finally { await TerminalizeOwnedAsync(good.Store, queued.Job.State.Id); }
    }

    [Fact]
    public async Task IndependentConnectionsEnqueueAndClaimOnceAndFenceRecovery()
    {
        await using var db = Context(); await EnsureNoForeignCandidatesAsync(db);
        var test = await SeedAsync(db);
        var key = "pg-batch-" + Guid.NewGuid().ToString("N");
        await using var other = Context(); var second = new EfCorePdfRenameStore(other, new EfCoreDocumentProcessingStore(other));
        var enqueued = await Task.WhenAll(test.Store.EnqueueAsync(PdfRenameRequests.Create(test.Preview, key)),
            second.EnqueueAsync(PdfRenameRequests.Create(test.Preview, key)));
        var id = enqueued[0].Job.State.Id;
        try
        {
            Assert.Single(enqueued, item => item.Created); Assert.Equal(id, enqueued[1].Job.State.Id);
            var now = DateTime.UtcNow;
            var claims = await Task.WhenAll(test.Store.ClaimAsync("first", now, TimeSpan.FromSeconds(5)),
                second.ClaimAsync("second", now, TimeSpan.FromSeconds(5)));
            var lease = Assert.Single(claims, item => item is not null)!;
            Assert.Equal(id, lease.Job.Id);
            var recovered = (await second.ClaimAsync("recovery", now.AddSeconds(6), TimeSpan.FromSeconds(5)))!;
            Assert.NotEqual(lease.Token, recovered.Token);
            Assert.False(await test.Store.HeartbeatAsync(id, lease.Token, lease.Owner, now.AddSeconds(7), TimeSpan.FromSeconds(5), "stale"));
            Assert.False(await test.Store.CompleteAsync(id, lease.Token, lease.Owner, now.AddSeconds(7), []));
            await Assert.ThrowsAsync<InvalidDataException>(() => second.CompleteAsync(id, recovered.Token, recovered.Owner, now.AddSeconds(7), []));
            await second.CancelAsync(id, now.AddSeconds(8));
            Assert.Null(await test.Store.ClaimAsync("recover-cancel", now.AddSeconds(12), TimeSpan.FromSeconds(5)));
            Assert.Equal(PdfProcessingJobStatus.Cancelled, (await second.GetJobAsync(id))!.State.Status);
            await AssertImmutableAsync(() => other.Set<PdfRenamePlanRecord>()
                .Where(row => row.Id == test.Preview.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Data, "altered")));
            await AssertImmutableAsync(() => other.Set<PdfRenameJobRecord>()
                .Where(row => row.Id == id).ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, PdfProcessingJobStatus.Pending)));
        }
        finally { await TerminalizeOwnedAsync(test.Store, id); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RealBatchAdoptionCommitsOrRollsBackEveryMountRevisionAndAudit(bool rejectAdvance)
    {
        await using var db = Context(); await EnsureNoForeignCandidatesAsync(db);
        var test = await SeedAsync(db, rejectAdvance);
        var queued = await test.Service.EnqueueAsync(test.Application.Id, "0001", test.Preview.Id, test.Preview.Plan.PlanDigest,
            "pg-adopt-" + test.Preview.Id);
        try
        {
            var lease = (await test.Store.ClaimAsync("pg-workflow", DateTime.UtcNow, TimeSpan.FromMinutes(2)))!;
            Assert.Equal(queued.Job.State.Id, lease.Job.Id);
            await test.Service.ExecuteAsync(lease);
            Assert.Equal(2, (await test.Store.GetJobAsync(lease.Job.Id))!.Results.Count);
            if (rejectAdvance)
                await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => test.Service.AdoptAsync(test.Application.Id, "0001", lease.Job.Id, 0));
            else
            {
                var adopted = await test.Service.AdoptAsync(test.Application.Id, "0001", lease.Job.Id, 0);
                Assert.Equal(2, adopted.Documents.Count);
                Assert.Equal(adopted.Documents.Select(item => item.DocumentId),
                    (await test.Service.AdoptAsync(test.Application.Id, "0001", lease.Job.Id, 0)).Documents.Select(item => item.DocumentId));
            }
            await using var read = Context();
            var reader = new EfCorePdfRenameStore(read, new EfCoreDocumentProcessingStore(read));
            var adoption = await reader.GetAdoptionAsync(lease.Job.Id);
            Assert.Equal(rejectAdvance, adoption is null);
            Assert.Equal(rejectAdvance ? 0 : 1, await new EfCoreWorkspaceRevisionStore(read).GetRevisionAsync(test.Application.Id, "0001"));
            foreach (var original in test.Mounts)
            {
                var loaded = (await new EfCoreDocumentPlacementRepository(read).GetAsync(original.Id))!;
                Assert.Equal(rejectAdvance, original.DocumentId == loaded.DocumentId);
                Assert.Equal(original.LeafId, loaded.LeafId);
                var document = (await new EfCoreDocumentRepository(read).GetAsync(original.DocumentId))!;
                Assert.Equal(document.Sha256, Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(document.StoragePath))).ToLowerInvariant());
            }
            Assert.Equal(rejectAdvance ? 0 : 1, await read.AuditLogs.CountAsync(row => row.EntityType == "PdfRenameBatch" && row.EntityId == lease.Job.Id.ToString("D")));
        }
        finally { await TerminalizeOwnedAsync(test.Store, queued.Job.State.Id); }
    }

    private static RAToolsDbContext Context() => new(new DbContextOptionsBuilder<RAToolsDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("RATOOLS_TEST_POSTGRES") ?? throw new InvalidOperationException("RATOOLS_TEST_POSTGRES is required.")).Options);
    private static async Task AssertImmutableAsync(Func<Task> operation)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(operation);
        while (error is not PostgresException && error.InnerException is not null) error = error.InnerException;
        Assert.Equal("55000", Assert.IsType<PostgresException>(error).SqlState);
    }
    private static async Task EnsureNoForeignCandidatesAsync(RAToolsDbContext db) =>
        Assert.Equal(0, await db.Set<PdfRenameJobRecord>().CountAsync(row => row.Status == PdfProcessingJobStatus.Pending || row.Status == PdfProcessingJobStatus.Running));
    private static async Task TerminalizeOwnedAsync(IPdfRenameStore store, Guid id)
    {
        var job = await store.CancelAsync(id, DateTime.UtcNow);
        if (job?.State.Status == PdfProcessingJobStatus.Running)
            await store.FailAsync(id, job.State.LeaseToken!.Value, job.State.LeaseOwner!, DateTime.UtcNow,
                PdfProcessingJobStatus.Cancelled, "TEST_FINISHED", "Task-owned test evidence retained.");
    }

    private static async Task<TestContext> SeedAsync(RAToolsDbContext db, bool rejectAdvance = false)
    {
        var root = Path.GetFullPath(".artifacts/p507-pg-tests/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "0001", "m5"));
        var application = new SubmissionApplication("p507-" + Guid.NewGuid().ToString("N"), "US", "Synthetic", root, "us-fda-ectd-3.2.2");
        application.CreateSequence("0001", "original", "Batch persistence test");
        var applications = new EfCoreApplicationRepository(db); await applications.AddAsync(application);
        var documents = new EfCoreDocumentRepository(db); var placements = new EfCoreDocumentPlacementRepository(db);
        var mounts = new List<DocumentPlacement>();
        var bytes = await File.ReadAllBytesAsync("tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        foreach (var name in new[] { "source.pdf", "old.pdf" })
        {
            var path = Path.Combine(root, "0001", "m5", name); await File.WriteAllBytesAsync(path, bytes);
            var document = new SubmissionDocument(name, "application/pdf", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), path); await documents.AddAsync(document);
            var mount = new DocumentPlacement(document.Id, application.Id, "0001", "m5.3.1", DocumentPlacementOperation.New, name);
            await placements.AddAsync(mount); mounts.Add(mount);
        }
        var versions = new EfCoreDocumentProcessingStore(db); var store = new EfCorePdfRenameStore(db, versions);
        var mutation = new WorkspaceMutationCoordinator(new ConditionalRevision(new EfCoreWorkspaceRevisionStore(db), rejectAdvance), new EfCorePersistenceTransaction(db));
        var options = new PdfProcessingExecutionOptions(); var files = new ProcessingFileStore(new Paths(root), options);
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions { ExecutablePath = Environment.GetEnvironmentVariable("RATOOLS_QPDF") ??
            Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe") });
        var inspector = new PdfPigPackageInspector();
        var previews = new PdfRenamePreviewService(applications, documents, placements, null!, null!, mutation, files, versions, engine, inspector, options);
        var service = new PdfRenameService(previews, store, versions, applications, documents, placements, null!, null!, mutation, files,
            engine, inspector, options, new EfCoreAuditLogRepository(db));
        var preview = await service.CreatePreviewAsync(application.Id, "0001", new(0, [new(mounts[1].Id, "renamed.pdf")], mounts.Select(item => item.Id).ToArray()));
        return new(application, mounts.ToArray(), preview, store, service);
    }
    private sealed record TestContext(SubmissionApplication Application, DocumentPlacement[] Mounts, PdfRenamePreview Preview, EfCorePdfRenameStore Store, PdfRenameService Service);
    private sealed class ConditionalRevision(IWorkspaceRevisionStore inner, bool reject) : IWorkspaceRevisionStore
    {
        public Task<IAsyncDisposable> LockApplicationAsync(Guid id, CancellationToken cancellationToken = default) => inner.LockApplicationAsync(id, cancellationToken);
        public Task<long?> GetRevisionAsync(Guid id, string sequence, CancellationToken cancellationToken = default) => inner.GetRevisionAsync(id, sequence, cancellationToken);
        public Task<bool> AdvanceAsync(Guid id, string sequence, long revision, CancellationToken cancellationToken = default) => reject ? Task.FromResult(false) : inner.AdvanceAsync(id, sequence, revision, cancellationToken);
    }
    private sealed class Paths(string root) : IWorkspacePathPolicy
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
