using RATools.Application.DocumentProcessing;
using RATools.Domain.Documents;
using RATools.Application.Documents;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.DocumentProcessing;

public sealed class ProcessingStoreSafetyTests
{
    private static readonly DateTime Now = new(2026, 11, 4, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ExpiredCancelledJobBecomesTerminalWithoutStartingAnotherAttempt()
    {
        var (store, plan, job) = await SeedAsync();
        var first = (await store.ClaimAsync("first", Now, TimeSpan.FromSeconds(10)))!;
        await store.CancelAsync(job.Id, Now.AddSeconds(1));
        Assert.Null(await store.ClaimAsync("second", Now.AddSeconds(11), TimeSpan.FromSeconds(10)));
        var cancelled = (await store.GetJobAsync(job.Id))!;
        Assert.Equal(PdfProcessingJobStatus.Cancelled, cancelled.Status);
        Assert.Equal(1, cancelled.AttemptCount);
        Assert.False(await store.CompleteAsync(job.Id, first.Token, first.Owner, Now.AddSeconds(12), Result(plan)));
    }

    [Fact]
    public async Task CrashedJobsStopAfterThreeAttemptsAndOldWorkerCannotComplete()
    {
        var (store, plan, job) = await SeedAsync();
        var first = (await store.ClaimAsync("first", Now, TimeSpan.FromSeconds(10)))!;
        Assert.NotNull(await store.ClaimAsync("second", Now.AddSeconds(11), TimeSpan.FromSeconds(10)));
        Assert.NotNull(await store.ClaimAsync("third", Now.AddSeconds(22), TimeSpan.FromSeconds(10)));
        Assert.Null(await store.ClaimAsync("fourth", Now.AddSeconds(33), TimeSpan.FromSeconds(10)));
        Assert.Equal(PdfProcessingJobStatus.Failed, (await store.GetJobAsync(job.Id))!.Status);
        Assert.False(await store.CompleteAsync(job.Id, first.Token, first.Owner, Now.AddSeconds(34), Result(plan)));
    }

    [Fact]
    public async Task CompletionCannotAttachAnOutputFromAnotherPlan()
    {
        var (store, plan, job) = await SeedAsync();
        var lease = (await store.ClaimAsync("worker", Now, TimeSpan.FromMinutes(1)))!;
        var unrelated = Result(plan with { PlanDigest = new string('c', 64) });
        await Assert.ThrowsAsync<InvalidDataException>(() => store.CompleteAsync(job.Id, lease.Token, lease.Owner, Now.AddSeconds(1), unrelated));
        Assert.Null(await store.GetVersionAsync(unrelated.Id));
        Assert.Equal(PdfProcessingJobStatus.Running, (await store.GetJobAsync(job.Id))!.Status);
    }

    [Fact]
    public async Task AlteredPlanDigestCannotBeStored()
    {
        var (store, plan, _) = await SeedAsync();
        await Assert.ThrowsAsync<InvalidDataException>(() => store.AddPlanAsync(plan with
        { Id = Guid.NewGuid(), ExpectedWorkspaceRevision = plan.ExpectedWorkspaceRevision + 1 }));
    }

    [Fact]
    public async Task AdoptionRecordRollsBackWithTheWorkspaceTransaction()
    {
        var (store, plan, job) = await SeedAsync();
        var adoption = new PdfProcessingAdoption(job.Id, plan.ApplicationId, plan.SequenceNumber, plan.PlacementId,
            Guid.NewGuid(), Guid.NewGuid(), 0, 1, Now);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new InMemoryPersistenceTransaction().ExecuteAsync(async ct =>
        {
            await store.AddAdoptionAsync(adoption, ct);
            throw new InvalidOperationException("Synthetic transaction failure.");
        }));
        Assert.Null(await store.GetAdoptionAsync(job.Id));
    }

    [Fact]
    public async Task CapturedSourceIsRejectedBeforeLegacyDeleteCanTouchFiles()
    {
        var (store, plan, _) = await SeedAsync();
        var repository = new InMemoryDocumentRepository();
        var version = (await store.GetVersionAsync(plan.SourceVersionId))!;
        var document = SubmissionDocument.Rehydrate(plan.SourceDocumentId, version.FileName, version.MediaType,
            version.FileSize, version.Sha256, version.Md5, version.StoragePath, Now);
        await repository.AddAsync(document);
        var applications = new InMemoryApplicationRepository();
        var placements = new InMemoryDocumentPlacementRepository();
        var revisions = new InMemoryWorkspaceRevisionStore(applications);
        var service = new DocumentService(repository, null!, placements, applications, null!, null!, null!,
            new RATools.Application.Workspaces.WorkspaceMutationCoordinator(revisions, new InMemoryPersistenceTransaction()), processing: store);
        await Assert.ThrowsAsync<DocumentDeleteConflictException>(() => service.DeleteAsync(document.Id));
        Assert.NotNull(await repository.GetAsync(document.Id));
        Assert.True(await store.HasApplicationEvidenceAsync(plan.ApplicationId, plan.SequenceNumber));
        Assert.True(await store.HasDocumentEvidenceAsync(document.Id));
    }

    private static async Task<(InMemoryDocumentProcessingStore Store, PdfProcessingPlan Plan, PdfProcessingJob Job)> SeedAsync()
    {
        var store = new InMemoryDocumentProcessingStore();
        var applicationId = Guid.NewGuid();
        var source = DocumentVersion.CreateSource(Guid.NewGuid(), applicationId, "0001", "source.pdf", "application/pdf",
            10, new string('a', 64), new string('b', 32), Path.GetFullPath("memory-only/source.pdf"), Now);
        await store.AddVersionAsync(source);
        var plan = PdfProcessingPlan.Create(source, applicationId, "0001", Guid.NewGuid(), 0,
            new PdfProcessingParameters("UseOutlines", [], []), "qpdf-12.4.1");
        await store.AddPlanAsync(plan);
        var job = PdfProcessingJob.Create(plan, "request");
        await store.EnqueueAsync(job);
        return (store, plan, job);
    }

    private static DocumentVersion Result(PdfProcessingPlan plan) => DocumentVersion.CreateResult(plan.ApplicationId,
        plan.SequenceNumber, plan.SourceDocumentId, plan.SourceVersionId, "source.pdf", "application/pdf", 12,
        new string('c', 64), new string('d', 32), Path.GetFullPath("memory-only/result.pdf"), Now,
        plan.EngineVersion, plan.ParametersJson, plan.PlanDigest);
}
