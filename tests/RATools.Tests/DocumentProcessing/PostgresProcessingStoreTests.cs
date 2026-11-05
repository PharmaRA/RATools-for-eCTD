using Microsoft.EntityFrameworkCore;
using Npgsql;
using RATools.Application.DocumentProcessing;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Tests.Persistence.Postgres;

namespace RATools.Tests.DocumentProcessing;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresProcessingStoreTests
{
    [RequiresPostgresFact]
    public async Task CorruptQueuedRequestBecomesTerminalAndDoesNotStarveTheNextJob()
    {
        await OnlyOwnedCandidateAsync(Guid.Empty, DateTime.UtcNow);
        var corruptPlan = await SeedAsync();
        var nextPlan = await SeedAsync();
        await using var context = Context();
        var corrupt = PdfProcessingJob.Create(corruptPlan, Guid.NewGuid().ToString("N"));
        context.Add(new PdfProcessingJobRecord { Id = corrupt.Id, PlanId = corrupt.PlanId, PlanDigest = corrupt.PlanDigest,
            IdempotencyKey = corrupt.IdempotencyKey, RequestDigest = new string('0', 64), Status = PdfProcessingJobStatus.Pending,
            Stage = "queued", CreatedUtc = DateTime.UtcNow.AddMinutes(-1) });
        await context.SaveChangesAsync();
        var store = new EfCoreDocumentProcessingStore(context);
        var next = (await store.EnqueueAsync(PdfProcessingJob.Create(nextPlan, Guid.NewGuid().ToString("N")))).Job;
        try
        {
            var lease = await store.ClaimAsync("integrity-test", DateTime.UtcNow, TimeSpan.FromMinutes(1));
            Assert.NotNull(lease);
            Assert.Equal(next.Id, lease.Job.Id);
            var failed = await context.Set<PdfProcessingJobRecord>().AsNoTracking().SingleAsync(row => row.Id == corrupt.Id);
            Assert.Equal(PdfProcessingJobStatus.Failed, failed.Status);
            Assert.Equal("REQUEST_INTEGRITY_FAILED", failed.FailureCode);
            Assert.True(await store.CompleteAsync(next.Id, lease.Token, lease.Owner, DateTime.UtcNow, Result(nextPlan)));
        }
        finally
        {
            // Retain the synthetic requests as evidence; leave neither eligible for another test.
            await context.Set<PdfProcessingJobRecord>().Where(row => (row.Id == corrupt.Id || row.Id == next.Id)
                && (row.Status == PdfProcessingJobStatus.Pending || row.Status == PdfProcessingJobStatus.Running))
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, PdfProcessingJobStatus.Cancelled)
                    .SetProperty(row => row.CancellationRequested, true).SetProperty(row => row.CompletedUtc, DateTime.UtcNow));
        }
    }

    [RequiresPostgresFact]
    public async Task IndependentStoresReplayAndClaimOnceThenFenceTheResult()
    {
        var plan = await SeedAsync();
        var key = "processing-" + Guid.NewGuid().ToString("N");
        await using var first = Context();
        await using var second = Context();
        var a = new EfCoreDocumentProcessingStore(first);
        var b = new EfCoreDocumentProcessingStore(second);
        var enqueued = await Task.WhenAll(a.EnqueueAsync(PdfProcessingJob.Create(plan, key)), b.EnqueueAsync(PdfProcessingJob.Create(plan, key)));
        Assert.Single(enqueued, item => item.Created);
        var job = enqueued[0].Job;
        Assert.Equal(job.Id, enqueued[1].Job.Id);
        var now = DateTime.UtcNow;
        await OnlyOwnedCandidateAsync(job.Id, now);
        var claimed = await Task.WhenAll(a.ClaimAsync("a", now, TimeSpan.FromMinutes(1)), b.ClaimAsync("b", now, TimeSpan.FromMinutes(1)));
        var lease = Assert.Single(claimed, item => item is not null)!;
        Assert.Equal(job.Id, lease.Job.Id);
        Assert.False(await b.HeartbeatAsync(job.Id, Guid.NewGuid(), "stale", now.AddSeconds(1), TimeSpan.FromMinutes(1), "check"));
        var wrong = Result(plan, new string('e', 64));
        await Assert.ThrowsAsync<InvalidDataException>(() => a.CompleteAsync(job.Id, lease.Token, lease.Owner, now.AddSeconds(2), wrong));
        Assert.Null(await a.GetVersionAsync(wrong.Id));
        var output = Result(plan);
        Assert.True(await b.CompleteAsync(job.Id, lease.Token, lease.Owner, now.AddSeconds(3), output));
        await using var reader = Context();
        var loaded = await new EfCoreDocumentProcessingStore(reader).GetVersionAsync(output.Id);
        Assert.Equal(output.Sha256, loaded!.Sha256);
        Assert.Equal(plan.SourceVersionId, loaded.ParentVersionId);
        Assert.Equal(PdfProcessingJobStatus.Completed, (await a.GetJobAsync(job.Id))!.Status);
        await AssertImmutableAsync(() => reader.Set<DocumentVersionRecord>().Where(row => row.Id == output.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Data, "altered")));
        await AssertImmutableAsync(() => reader.Set<PdfProcessingJobRecord>().Where(row => row.Id == job.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, PdfProcessingJobStatus.Pending)));
    }

    [RequiresPostgresFact]
    public async Task CancelledExpiredAttemptDoesNotRestartAndCrashRetriesAreBounded()
    {
        await using var context = Context();
        var store = new EfCoreDocumentProcessingStore(context);
        var plan = await SeedAsync();
        var job = (await store.EnqueueAsync(PdfProcessingJob.Create(plan, Guid.NewGuid().ToString("N")))).Job;
        var now = DateTime.UtcNow;
        await OnlyOwnedCandidateAsync(job.Id, now);
        var lease = (await store.ClaimAsync("first", now, TimeSpan.FromSeconds(10)))!;
        await store.CancelAsync(job.Id, now.AddSeconds(1));
        Assert.Null(await store.ClaimAsync("recovery", now.AddSeconds(11), TimeSpan.FromSeconds(10)));
        Assert.Equal(PdfProcessingJobStatus.Cancelled, (await store.GetJobAsync(job.Id))!.Status);
        Assert.False(await store.CompleteAsync(job.Id, lease.Token, lease.Owner, now.AddSeconds(12), Result(plan)));

        var secondPlan = await SeedAsync();
        var secondJob = (await store.EnqueueAsync(PdfProcessingJob.Create(secondPlan, Guid.NewGuid().ToString("N")))).Job;
        await OnlyOwnedCandidateAsync(secondJob.Id, now);
        Assert.NotNull(await store.ClaimAsync("attempt1", now, TimeSpan.FromSeconds(10)));
        Assert.NotNull(await store.ClaimAsync("attempt2", now.AddSeconds(11), TimeSpan.FromSeconds(10)));
        Assert.NotNull(await store.ClaimAsync("attempt3", now.AddSeconds(22), TimeSpan.FromSeconds(10)));
        Assert.Null(await store.ClaimAsync("attempt4", now.AddSeconds(33), TimeSpan.FromSeconds(10)));
        var exhausted = (await store.GetJobAsync(secondJob.Id))!;
        Assert.Equal(PdfProcessingJobStatus.Failed, exhausted.Status);
        Assert.Equal(3, exhausted.AttemptCount);
        Assert.Equal("ATTEMPTS_EXHAUSTED", exhausted.FailureCode);
    }

    [RequiresPostgresFact]
    public async Task AdoptionIsPartOfTheOuterTransactionAndCommittedEvidenceCannotChange()
    {
        var plan = await SeedAsync();
        await using var context = Context();
        var store = new EfCoreDocumentProcessingStore(context);
        var job = (await store.EnqueueAsync(PdfProcessingJob.Create(plan, Guid.NewGuid().ToString("N")))).Job;
        var now = DateTime.UtcNow;
        await OnlyOwnedCandidateAsync(job.Id, now);
        var lease = (await store.ClaimAsync("worker", now, TimeSpan.FromMinutes(1)))!;
        var result = Result(plan);
        Assert.True(await store.CompleteAsync(job.Id, lease.Token, lease.Owner, now.AddSeconds(1), result));
        var newDocument = new SubmissionDocument("source.pdf", "application/pdf", result.FileSize, result.Sha256, result.Md5,
            Path.GetFullPath("memory-only/adopted.pdf"));
        await new EfCoreDocumentRepository(context).AddAsync(newDocument);
        var adoption = new PdfProcessingAdoption(job.Id, plan.ApplicationId, plan.SequenceNumber, plan.PlacementId,
            newDocument.Id, result.Id, 0, 1, now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AddAdoptionAsync(adoption));
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await store.AddAdoptionAsync(adoption);
            await transaction.RollbackAsync();
        }
        context.ChangeTracker.Clear();
        Assert.Null(await store.GetAdoptionAsync(job.Id));
        await using (var transaction = await context.Database.BeginTransactionAsync())
        {
            await store.AddAdoptionAsync(adoption);
            await transaction.CommitAsync();
        }
        var committed = (await store.GetAdoptionAsync(job.Id))!;
        Assert.Equal(newDocument.Id, committed.DocumentId);
        Assert.Equal(1, committed.WorkspaceRevision);
        await AssertImmutableAsync(() => context.Set<PdfProcessingAdoptionRecord>().Where(row => row.JobId == job.Id)
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Data, "altered")));
    }

    private static RAToolsDbContext Context() => new PostgresFixture().CreateDbContext();

    private static async Task AssertImmutableAsync(Func<Task> write)
    {
        var error = await Assert.ThrowsAnyAsync<Exception>(write);
        // Npgsql's execution strategy may wrap object_not_in_prerequisite_state.
        // Prove the actual database guard fired rather than accepting any error.
        while (error is not PostgresException && error.InnerException is not null) error = error.InnerException;
        Assert.Equal("55000", Assert.IsType<PostgresException>(error).SqlState);
    }

    private static async Task OnlyOwnedCandidateAsync(Guid id, DateTime now)
    {
        await using var context = Context();
        Assert.False(await context.Set<PdfProcessingJobRecord>().AnyAsync(row => row.Id != id &&
            (row.Status == PdfProcessingJobStatus.Pending || (row.Status == PdfProcessingJobStatus.Running && row.LeaseExpiresUtc <= now))),
            "Use the serial PostgreSQL test database without live workers; another eligible processing job exists.");
    }

    private static async Task<PdfProcessingPlan> SeedAsync()
    {
        await using var context = Context();
        var application = new SubmissionApplication("processing-" + Guid.NewGuid().ToString("N"), "US", "Synthetic", Path.GetFullPath("memory-only"), "us-fda-ectd-3.2.2");
        application.CreateSequence("0001", "original", "Processing test");
        await new EfCoreApplicationRepository(context).AddAsync(application);
        var document = new SubmissionDocument("source.pdf", "application/pdf", 10, new string('a', 64), new string('b', 32), Path.GetFullPath("memory-only/source.pdf"));
        await new EfCoreDocumentRepository(context).AddAsync(document);
        var source = DocumentVersion.CreateSource(document.Id, application.Id, "0001", document.FileName, document.MediaType,
            document.FileSize, document.Sha256, document.Md5, document.StoragePath, DateTime.UtcNow);
        var store = new EfCoreDocumentProcessingStore(context);
        await store.AddVersionAsync(source);
        var plan = PdfProcessingPlan.Create(source, application.Id, "0001", Guid.NewGuid(), 0,
            new PdfProcessingParameters("UseOutlines", [], []), "qpdf-12.4.1");
        await store.AddPlanAsync(plan);
        return plan;
    }

    private static DocumentVersion Result(PdfProcessingPlan plan, string? digest = null) => DocumentVersion.CreateResult(plan.ApplicationId,
        plan.SequenceNumber, plan.SourceDocumentId, plan.SourceVersionId, "source.pdf", "application/pdf", 12,
        new string('c', 64), new string('d', 32), Path.GetFullPath("memory-only/result.pdf"), DateTime.UtcNow,
        plan.EngineVersion, plan.ParametersJson, digest ?? plan.PlanDigest);
}
