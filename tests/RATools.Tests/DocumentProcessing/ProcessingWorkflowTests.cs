using System.Security.Cryptography;
using RATools.Application.Abstractions.Security;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.DocumentProcessing;
using RATools.Application.Documents;
using RATools.Application.Finalization;
using RATools.Application.Publishing;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.DocumentProcessing;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.DocumentProcessing;

public sealed class ProcessingWorkflowTests
{
    private static readonly string[] ExpectedBookmarkTitles = ["总览", "新增子章节", "另一个根节点"];
    private static readonly int[] ExpectedBookmarkLevels = [0, 1, 0];
    [Fact]
    public async Task RealNavigationResultIsAdoptedAsNewBytesWithTheSameLeafAndDeliveryPath()
    {
        var test = await SetupAsync();
        var sourceBytes = await File.ReadAllBytesAsync(test.Document.StoragePath);
        var inspection = await test.Service.InspectAsync(test.Application.Id, "0001", test.Placement.Id, 0);
        var originalLink = inspection.Navigation.Links.Single(link => link.ObjectPath == "page[1]/annotation[1]");
        var parameters = new PdfProcessingParameters("UseOutlines",
            [new("总览", 2, null, 0), new("新增子章节", 1, null, 1), new("另一个根节点", 1, null, 0)],
            [new(1, "2", PdfLinkTargetKind.Page, originalLink.ObjectPath, originalLink.Target)]);
        var plan = await test.Service.CreatePlanAsync(test.Application.Id, "0001", test.Placement.Id, new(inspection.SourceVersionId, 0, parameters));
        var first = await test.Service.EnqueueAsync(test.Application.Id, "0001", plan.Id, plan.PlanDigest, "workflow-key");
        var replay = await test.Service.EnqueueAsync(test.Application.Id, "0001", plan.Id, plan.PlanDigest, "workflow-key");
        Assert.Equal(first.Job.Id, replay.Job.Id);
        var lease = (await test.Store.ClaimAsync("test", DateTime.UtcNow, TimeSpan.FromMinutes(2)))!;
        await test.Service.ExecuteAsync(lease);
        var completed = await test.Service.GetJobAsync(test.Application.Id, "0001", first.Job.Id);
        Assert.Equal(PdfProcessingJobStatus.Completed, completed.Status);
        Assert.Equal(test.Document.Id, (await test.Placements.GetAsync(test.Placement.Id))!.DocumentId);
        var result = (await test.Store.GetVersionAsync(completed.ResultVersionId!.Value))!;
        var checkedResult = await test.Engine.InspectAsync(result.StoragePath, 8 * 1024 * 1024);
        Assert.Equal(ExpectedBookmarkTitles, checkedResult.Bookmarks.Select(item => item.Title));
        Assert.Equal(ExpectedBookmarkLevels, checkedResult.Bookmarks.Select(item => item.Level));
        var adopted = await test.Service.AdoptAsync(test.Application.Id, "0001", first.Job.Id, 0);
        var repeated = await test.Service.AdoptAsync(test.Application.Id, "0001", first.Job.Id, 0);
        Assert.Equal(adopted.DocumentId, repeated.DocumentId);
        var enqueueReplay = await test.Service.EnqueueAsync(test.Application.Id, "0001", plan.Id, plan.PlanDigest, "workflow-key");
        Assert.Equal(first.Job.Id, enqueueReplay.Job.Id);
        Assert.Equal(1, await test.Revisions.GetRevisionAsync(test.Application.Id, "0001"));
        var placement = (await test.Placements.GetAsync(test.Placement.Id))!;
        Assert.Equal(test.Placement.LeafId, placement.LeafId);
        Assert.Equal(test.Placement.Operation, placement.Operation);
        Assert.Equal(test.Placement.LifecycleTargetPlacementId, placement.LifecycleTargetPlacementId);
        Assert.NotEqual(test.Document.Id, placement.DocumentId);
        var document = (await test.Documents.GetAsync(placement.DocumentId))!;
        Assert.Equal("m3/source.pdf", PublishOutputNaming.BuildPublishedDocumentRelativePath(document, "0001"));
        Assert.Equal(result.Sha256, document.Sha256);
        Assert.Equal(result.Md5, document.Md5);
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync(test.Document.StoragePath));
        Assert.Equal(sourceBytes, await File.ReadAllBytesAsync((await test.Store.GetVersionAsync(inspection.SourceVersionId))!.StoragePath));
        await File.WriteAllTextAsync(document.StoragePath, "changed draft copy");
        await test.Files.VerifyAsync(result);
    }

    [Fact]
    public async Task ChangedRevisionAndTamperedOutputCannotBeAdopted()
    {
        var test = await SetupAsync();
        var inspection = await test.Service.InspectAsync(test.Application.Id, "0001", test.Placement.Id, 0);
        var plan = await test.Service.CreatePlanAsync(test.Application.Id, "0001", test.Placement.Id,
            new(inspection.SourceVersionId, 0, new("UseOutlines", null, null)));
        var queued = await test.Service.EnqueueAsync(test.Application.Id, "0001", plan.Id, plan.PlanDigest, "tamper-key");
        var lease = (await test.Store.ClaimAsync("test", DateTime.UtcNow, TimeSpan.FromMinutes(2)))!;
        await test.Service.ExecuteAsync(lease);
        var result = (await test.Store.GetVersionAsync((await test.Store.GetJobAsync(queued.Job.Id))!.ResultVersionId!.Value))!;
        await Assert.ThrowsAsync<PdfProcessingRejectedException>(() => test.Service.AdoptAsync(test.Application.Id, "0001", queued.Job.Id, 1));
        await File.WriteAllTextAsync(result.StoragePath, "tampered result");
        await Assert.ThrowsAsync<InvalidDataException>(() => test.Service.AdoptAsync(test.Application.Id, "0001", queued.Job.Id, 0));
        Assert.Equal(test.Document.Id, (await test.Placements.GetAsync(test.Placement.Id))!.DocumentId);
        Assert.Equal(0, await test.Revisions.GetRevisionAsync(test.Application.Id, "0001"));
        Assert.Null(await test.Store.GetAdoptionAsync(queued.Job.Id));
    }

    [Fact]
    public async Task AnotherApplicationCannotReadCapturedVersionsOrUseTheirPlans()
    {
        var test = await SetupAsync();
        var inspection = await test.Service.InspectAsync(test.Application.Id, "0001", test.Placement.Id, 0);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => test.Service.OpenVersionAsync(Guid.NewGuid(), "0001", inspection.SourceVersionId));
        var plan = await test.Service.CreatePlanAsync(test.Application.Id, "0001", test.Placement.Id,
            new(inspection.SourceVersionId, 0, new("UseOutlines", null, null)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => test.Service.EnqueueAsync(Guid.NewGuid(), "0001", plan.Id, plan.PlanDigest, "foreign"));
    }

    [Fact]
    public async Task SimultaneousAdoptionRetriesReturnOneCommittedResult()
    {
        var test = await SetupAsync();
        var job = await CompleteAsync(test);
        test.Revisions.GateNextTwoLocks();
        var results = await Task.WhenAll(test.Service.AdoptAsync(test.Application.Id, "0001", job, 0),
            test.Service.AdoptAsync(test.Application.Id, "0001", job, 0));
        Assert.Equal(results[0].DocumentId, results[1].DocumentId);
        Assert.Equal(1, await test.Revisions.GetRevisionAsync(test.Application.Id, "0001"));
        Assert.Equal(3, (await test.Documents.ListAsync()).Count);
    }

    [Fact]
    public async Task FailedRevisionCommitRollsBackDocumentPlacementAndAdoptionTogether()
    {
        var test = await SetupAsync();
        var job = await CompleteAsync(test);
        test.Revisions.RejectAdvance = true;
        await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => test.Service.AdoptAsync(test.Application.Id, "0001", job, 0));
        Assert.Equal(2, (await test.Documents.ListAsync()).Count);
        Assert.Equal(test.Document.Id, (await test.Placements.GetAsync(test.Placement.Id))!.DocumentId);
        Assert.Equal(0, await test.Revisions.GetRevisionAsync(test.Application.Id, "0001"));
        Assert.Null(await test.Store.GetAdoptionAsync(job));
    }

    [Fact]
    public async Task FinalizingTheWorkspaceBeforeAdoptionLeavesItsOriginalContentUnchanged()
    {
        var test = await SetupAsync();
        var job = await CompleteAsync(test);
        var snapshot = FinalizedSequence.Create(test.Application.Id, "0001", 0, new string('a', 64), "test-profile", "3.2.2", "3.3",
            new string('b', 64), Guid.NewGuid(), new string('c', 64), new string('d', 64), PackageValidationMode.Development,
            false, ["NotQualified"], "test", Guid.NewGuid().ToString("N"), DateTime.UtcNow);
        await test.Finalizations.RegisterAsync(snapshot);
        await Assert.ThrowsAsync<WorkspaceFinalizedException>(() => test.Service.AdoptAsync(test.Application.Id, "0001", job, 0));
        Assert.Equal(test.Document.Id, (await test.Placements.GetAsync(test.Placement.Id))!.DocumentId);
        Assert.Null(await test.Store.GetAdoptionAsync(job));
        Assert.Equal(snapshot.ManifestDigest, (await test.Finalizations.GetAsync(snapshot.Id))!.ManifestDigest);
    }

    [Theory]
    [InlineData("old.pdf#page=99")]
    [InlineData("old.pdf#nameddest=missing")]
    public async Task RemoteTargetMustResolveBeforeThePlanCanBeApproved(string target)
    {
        var test = await SetupAsync();
        var inspected = await test.Service.InspectAsync(test.Application.Id, "0001", test.Placement.Id, 0);
        var link = inspected.Navigation.Links.Single(item => item.ObjectPath == "page[1]/annotation[2]");
        await Assert.ThrowsAsync<PdfProcessingRejectedException>(() => test.Service.CreatePlanAsync(test.Application.Id, "0001", test.Placement.Id,
            new(inspected.SourceVersionId, 0, new(null, null, [new(1, target, PdfLinkTargetKind.RelativeRemotePdf, link.ObjectPath, link.Target)]))));
    }

    private static async Task<Guid> CompleteAsync(Workflow test)
    {
        var source = await test.Service.InspectAsync(test.Application.Id, "0001", test.Placement.Id, 0);
        var plan = await test.Service.CreatePlanAsync(test.Application.Id, "0001", test.Placement.Id,
            new(source.SourceVersionId, 0, new("UseOutlines", null, null)));
        var job = await test.Service.EnqueueAsync(test.Application.Id, "0001", plan.Id, plan.PlanDigest, Guid.NewGuid().ToString("N"));
        var lease = (await test.Store.ClaimAsync("test", DateTime.UtcNow, TimeSpan.FromMinutes(2)))!;
        await test.Service.ExecuteAsync(lease);
        return job.Job.Id;
    }

    private static async Task<Workflow> SetupAsync()
    {
        var directory = Path.GetFullPath($".artifacts/p5-06-workflow-tests/{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "0001", "m3"));
        var application = new SubmissionApplication(Guid.NewGuid().ToString("N"), "US", "Synthetic", directory, "us-fda-ectd-3.2.2");
        application.CreateSequence("0001", "original", "test");
        var applications = new InMemoryApplicationRepository();
        await applications.AddAsync(application);
        var documents = new InMemoryDocumentRepository();
        var placements = new InMemoryDocumentPlacementRepository();
        var bytes = await File.ReadAllBytesAsync("tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        var md5 = Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
        SubmissionDocument? source = null; DocumentPlacement? selected = null;
        foreach (var fileName in new[] { "source.pdf", "old.pdf" })
        {
            var path = Path.Combine(directory, "0001", "m3", fileName);
            await File.WriteAllBytesAsync(path, bytes);
            var document = new SubmissionDocument(fileName, "application/pdf", bytes.Length, sha, md5, path);
            await documents.AddAsync(document);
            var placement = new DocumentPlacement(document.Id, application.Id, "0001", "m3.2.s.4.1", DocumentPlacementOperation.New, fileName);
            await placements.AddAsync(placement);
            if (fileName == "source.pdf") { source = document; selected = placement; }
        }
        var revisions = new ControlledRevisions(new InMemoryWorkspaceRevisionStore(applications));
        var finalizations = new InMemoryFinalizationStore();
        var mutation = new WorkspaceMutationCoordinator(revisions, new InMemoryPersistenceTransaction(),
            finalizedGuard: new FinalizedWorkspaceGuard(finalizations));
        var store = new InMemoryDocumentProcessingStore();
        var options = new PdfProcessingExecutionOptions();
        var paths = new TestPaths(directory);
        var files = new ProcessingFileStore(paths, options);
        var executable = Environment.GetEnvironmentVariable("RATOOLS_QPDF")
            ?? Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe");
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions { ExecutablePath = executable });
        var service = new DocumentProcessingService(store, applications, documents, placements, mutation,
            new DocumentStorageBoundary(paths), files, engine, new PdfPigPackageInspector(), options);
        return new(application, source!, selected!, service, store, documents, placements, revisions, files, engine, finalizations);
    }

    private sealed record Workflow(SubmissionApplication Application, SubmissionDocument Document, DocumentPlacement Placement,
        DocumentProcessingService Service, InMemoryDocumentProcessingStore Store, InMemoryDocumentRepository Documents,
        InMemoryDocumentPlacementRepository Placements, ControlledRevisions Revisions,
        ProcessingFileStore Files, QpdfProcessingEngine Engine, InMemoryFinalizationStore Finalizations);

    private sealed class ControlledRevisions(IWorkspaceRevisionStore inner) : IWorkspaceRevisionStore
    {
        private TaskCompletionSource? _barrier;
        private int _arrivals;
        public bool RejectAdvance { get; set; }
        public void GateNextTwoLocks() { _arrivals = 0; _barrier = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public async Task<IAsyncDisposable> LockApplicationAsync(Guid id, CancellationToken cancellationToken = default)
        {
            var barrier = _barrier;
            if (barrier is not null)
            {
                if (Interlocked.Increment(ref _arrivals) == 2) { _barrier = null; barrier.SetResult(); }
                await barrier.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return await inner.LockApplicationAsync(id, cancellationToken);
        }
        public Task<long?> GetRevisionAsync(Guid id, string sequence, CancellationToken cancellationToken = default) =>
            inner.GetRevisionAsync(id, sequence, cancellationToken);
        public Task<bool> AdvanceAsync(Guid id, string sequence, long revision, CancellationToken cancellationToken = default) =>
            RejectAdvance ? Task.FromResult(false) : inner.AdvanceAsync(id, sequence, revision, cancellationToken);
    }

    private sealed class TestPaths(string root) : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [root];
        public string EnsureAllowed(string path)
        {
            var full = Path.GetFullPath(path);
            if (full != root && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new InvalidOperationException("Outside test workspace.");
            return full;
        }
    }
}
