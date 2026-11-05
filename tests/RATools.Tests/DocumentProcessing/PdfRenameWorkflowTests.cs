using System.Security.Cryptography;
using RATools.Application.Abstractions.Security;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.DocumentProcessing;
using RATools.Application.Ctd;
using RATools.Application.Documents;
using RATools.Application.Documents.Requests;
using RATools.Application.Validation;
using RATools.Application.Publishing;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Domain.Ctd;
using RATools.Infrastructure.DocumentProcessing;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.DocumentProcessing;

public sealed class PdfRenameWorkflowTests
{
    [Fact]
    public async Task SourceAndTargetMovesUseFinalBusinessPathsAndDirectMutationEntrypointsRequirePreview()
    {
        var test = await SetupAsync(withNodes: true);
        var leaves = test.Workspace!.Nodes.Where(item => item.CtdSection == "m3.2.s.4.1").ToArray();
        await Assert.ThrowsAsync<PdfProcessingRejectedException>(() => test.Metadata.UpdateMetadataAsync(test.Mounts[1].Id,
            new UpdateDocumentPlacementMetadataRequest("Target", "New", "renamed", null, 1)));
        await Assert.ThrowsAsync<PdfProcessingRejectedException>(() => test.NodeMoves!.MoveAsync(test.Mounts[0].Id, new(leaves[0].NodeInstanceId, 7, 1)));
        Assert.Equal(1, await test.Revisions.GetRevisionAsync(test.Application.Id, "0001"));
        var plan = await test.Service.CreatePreviewAsync(test.Application.Id, "0001", new(1,
            [new(test.Mounts[0].Id, "source.pdf", leaves[0].NodeInstanceId), new(test.Mounts[1].Id, "renamed.pdf", leaves[1].NodeInstanceId)],
            test.Mounts.Select(item => item.Id).ToArray()));
        Assert.True(plan.Plan.CanExecute, string.Join(";", plan.Plan.Issues.Select(item => item.Code)));
        var queued = await test.Service.EnqueueAsync(test.Application.Id, "0001", plan.Id, plan.Plan.PlanDigest, "move-" + plan.Id);
        await test.Service.ExecuteAsync((await test.Store.ClaimAsync("moves", DateTime.UtcNow, TimeSpan.FromMinutes(2)))!);
        var adopted = await test.Service.AdoptAsync(test.Application.Id, "0001", queued.Job.State.Id, 1);
        Assert.Equal(2, adopted.WorkspaceRevision);
        var source = (await test.Placements.GetAsync(test.Mounts[0].Id))!;
        var target = (await test.Placements.GetAsync(test.Mounts[1].Id))!;
        Assert.Equal(leaves[0].NodeInstanceId, source.NodeInstanceId); Assert.Equal(leaves[1].NodeInstanceId, target.NodeInstanceId);
        Assert.Equal("m3.2.s.4.1", source.CtdSection); Assert.Equal("m3.2.s.4.1", target.CtdSection);
        Assert.Equal(test.Mounts[0].SortOrder, source.SortOrder);
        var navigation = await InspectAdoptedAsync(test, source.DocumentId);
        Assert.Contains(navigation.Links, link => link.FileSpecification is not null && link.FileSpecification.StartsWith("../../../drug-a-beta/", StringComparison.Ordinal)
            && link.FileSpecification.EndsWith("/renamed.pdf", StringComparison.Ordinal));
        var targetNavigation = await InspectAdoptedAsync(test, target.DocumentId);
        Assert.Contains(targetNavigation.Links, link => link.FileSpecification == "renamed.pdf");
    }

    [Fact]
    public async Task DirectGuardPreservesNoImpactRenameAndRejectsAmbiguousUnresolvedInventory()
    {
        var test = await SetupAsync();
        // No incoming links reference source.pdf; its parent directory stays put.
        await test.Guard.EnsureNoImpactAsync(test.Mounts[0], "m5/source-renamed.pdf");
        await Assert.ThrowsAsync<PdfProcessingRejectedException>(() => test.Guard.EnsureNoImpactAsync(test.Mounts[1], "m5/target-renamed.pdf"));
        Assert.Equal(0, await test.Revisions.GetRevisionAsync(test.Application.Id, "0001"));
    }
    [Fact]
    public async Task BatchAdoptsEveryOutputAtOneRevisionAndReplayRetainsOriginalFilesAndLeafIdentities()
    {
        var test = await SetupAsync();
        var originals = await Task.WhenAll(test.Mounts.Select(async mount =>
            await File.ReadAllBytesAsync((await test.Documents.GetAsync(mount.DocumentId))!.StoragePath)));
        var preview = await PreviewAsync(test);
        Assert.True(preview.Plan.CanExecute, string.Join(";", preview.Plan.Issues.Select(issue => issue.Code)));
        Assert.Equal(preview.Plan.PlanDigest, (await test.Service.GetPreviewAsync(test.Application.Id, "0001", preview.Id)).Plan.PlanDigest);
        var queued = await test.Service.EnqueueAsync(test.Application.Id, "0001", preview.Id, preview.Plan.PlanDigest, "replay-" + preview.Id);
        var lease = (await test.Store.ClaimAsync("workflow", DateTime.UtcNow, TimeSpan.FromMinutes(2)))!;
        await test.Service.ExecuteAsync(lease);
        var complete = await test.Service.GetJobAsync(test.Application.Id, "0001", queued.Job.State.Id);
        Assert.Equal(PdfProcessingJobStatus.Completed, complete.Status);
        Assert.Equal(2, complete.Results.Count);
        Assert.All(test.Mounts, original => Assert.Equal(original.DocumentId, test.Placements.GetAsync(original.Id).Result!.DocumentId));
        var adoptions = await Task.WhenAll(test.Service.AdoptAsync(test.Application.Id, "0001", complete.Id, 0),
            test.Service.AdoptAsync(test.Application.Id, "0001", complete.Id, 0));
        Assert.Equal(adoptions[0].Documents.Select(item => item.DocumentId), adoptions[1].Documents.Select(item => item.DocumentId));
        Assert.Equal(1, await test.Revisions.GetRevisionAsync(test.Application.Id, "0001"));
        Assert.Equal(complete.Id, (await test.Service.EnqueueAsync(test.Application.Id, "0001", preview.Id, preview.Plan.PlanDigest, "replay-" + preview.Id)).Job.State.Id);
        for (var index = 0; index < test.Mounts.Length; index++)
        {
            var original = test.Mounts[index];
            var adopted = (await test.Placements.GetAsync(original.Id))!;
            Assert.Equal(original.LeafId, adopted.LeafId);
            Assert.Equal(original.Operation, adopted.Operation);
            Assert.Equal(original.LifecycleTargetPlacementId, adopted.LifecycleTargetPlacementId);
            Assert.NotEqual(original.DocumentId, adopted.DocumentId);
            var oldDocument = (await test.Documents.GetAsync(original.DocumentId))!;
            Assert.Equal(originals[index], await File.ReadAllBytesAsync(oldDocument.StoragePath));
            var draft = (await test.Documents.GetAsync(adopted.DocumentId))!;
            Assert.Equal(index == 0 ? "m5/source.pdf" : "m5/renamed.pdf", PublishOutputNaming.BuildPublishedDocumentRelativePath(draft, "0001"));
            var navigation = await test.Engine.InspectAsync(draft.StoragePath, 16 * 1024 * 1024);
            Assert.Contains(navigation.Links, link => link.FileSpecification == "renamed.pdf");
        }
    }

    [Fact]
    public async Task NewlyAddedReferencingDocumentInvalidatesTheCompleteInventoryBeforeExecution()
    {
        var test = await SetupAsync();
        var preview = await PreviewAsync(test);
        await AddAsync(test, "late.pdf");
        await Assert.ThrowsAsync<PdfProcessingRejectedException>(() => test.Service.EnqueueAsync(test.Application.Id, "0001", preview.Id,
            preview.Plan.PlanDigest, "stale-" + preview.Id));
        Assert.Null(await test.Service.GetPlanJobAsync(test.Application.Id, "0001", preview.Id));
    }

    [Fact]
    public async Task SecondPlacementFailureRollsBackAllMountsAdoptionAndRevision()
    {
        var test = await SetupAsync();
        var preview = await PreviewAsync(test);
        var queued = await test.Service.EnqueueAsync(test.Application.Id, "0001", preview.Id, preview.Plan.PlanDigest, "rollback-" + preview.Id);
        await test.Service.ExecuteAsync((await test.Store.ClaimAsync("rollback", DateTime.UtcNow, TimeSpan.FromMinutes(2)))!);
        test.Placements.FailSecondUpdate = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => test.Service.AdoptAsync(test.Application.Id, "0001", queued.Job.State.Id, 0));
        foreach (var original in test.Mounts)
            Assert.Equal(original.DocumentId, (await test.Placements.GetAsync(original.Id))!.DocumentId);
        Assert.Equal(0, await test.Revisions.GetRevisionAsync(test.Application.Id, "0001"));
        Assert.Null(await test.Store.GetAdoptionAsync(queued.Job.State.Id));
        Assert.Equal(2, (await test.Documents.ListAsync()).Count);
        test.Placements.FailSecondUpdate = false;
        Assert.Equal(2, (await test.Service.AdoptAsync(test.Application.Id, "0001", queued.Job.State.Id, 0)).Documents.Count);
    }

    [Fact]
    public async Task BatchStoreFencesExpiredWorkersAndRejectsPartialOutputSets()
    {
        var versions = new InMemoryDocumentProcessingStore();
        var store = new InMemoryPdfRenameStore(versions);
        var application = Guid.NewGuid(); var placement = Guid.NewGuid(); var version = Guid.NewGuid();
        var plan = new PdfRenamePlan(application, "0001", 0, [new(placement, "m5/new.pdf")], [placement], [placement], [], [], "");
        var now = DateTime.UtcNow;
        var preview = new PdfRenamePreview(Guid.NewGuid(), plan,
            [new(placement, placement, version, "0001", "m5/old.pdf", new string('a', 64), true)],
            [new(placement, "new.pdf")], "qpdf-12.4.1", now, now.AddHours(24), []);
        preview = preview with { Plan = plan with { PlanDigest = preview.ComputeDigest() } };
        await store.AddPlanAsync(preview);
        var queued = await store.EnqueueAsync(PdfRenameStore.Request(preview, "lease-" + Guid.NewGuid().ToString("N")));
        var first = (await store.ClaimAsync("first", now, TimeSpan.FromSeconds(5)))!;
        var second = (await store.ClaimAsync("second", now.AddSeconds(6), TimeSpan.FromSeconds(5)))!;
        Assert.Equal(first.Job.Id, second.Job.Id);
        Assert.NotEqual(first.Token, second.Token);
        Assert.False(await store.CompleteAsync(first.Job.Id, first.Token, first.Owner, now.AddSeconds(7), []));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.CompleteAsync(second.Job.Id, second.Token, second.Owner, now.AddSeconds(7), []));
        await store.CancelAsync(queued.Job.State.Id, now.AddSeconds(8));
        Assert.False(await store.CompleteAsync(second.Job.Id, second.Token, second.Owner, now.AddSeconds(8), []));
        Assert.Null(await store.ClaimAsync("third", now.AddSeconds(12), TimeSpan.FromSeconds(5)));
        Assert.Equal(PdfProcessingJobStatus.Cancelled, (await store.GetJobAsync(first.Job.Id))!.State.Status);
    }

    private static Task<PdfRenamePreview> PreviewAsync(Context test) => test.Service.CreatePreviewAsync(test.Application.Id, "0001",
        new(0, [new(test.Mounts[1].Id, "renamed.pdf")], test.Mounts.Select(item => item.Id).ToArray()));

    private static async Task<PdfNavigationInspection> InspectAdoptedAsync(Context test, Guid documentId)
    {
        var document = (await test.Documents.GetAsync(documentId))!;
        try { return await test.Engine.InspectAsync(document.StoragePath, 16 * 1024 * 1024); }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException($"Adopted test PDF could not reopen (path length {document.StoragePath.Length}): {document.StoragePath}", exception);
        }
    }

    private static async Task<Context> SetupAsync(bool withNodes = false)
    {
        var root = Path.GetFullPath(".artifacts/p507-workflows/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "0001", "m5"));
        var application = new SubmissionApplication("rename-" + Guid.NewGuid().ToString("N"), "US", "Synthetic", root, "us-fda-ectd-3.2.2");
        application.CreateSequence("0001", "original", "Rename test");
        var applications = new InMemoryApplicationRepository(); await applications.AddAsync(application);
        var documents = new InMemoryDocumentRepository(); var placements = new Placements();
        var versions = new InMemoryDocumentProcessingStore(); var store = new InMemoryPdfRenameStore(versions);
        var revisions = new InMemoryWorkspaceRevisionStore(applications);
        var mutation = new WorkspaceMutationCoordinator(revisions, new InMemoryPersistenceTransaction());
        var options = new PdfProcessingExecutionOptions(); var pathPolicy = new Paths(root); var files = new ProcessingFileStore(pathPolicy, options);
        var nodeRepository = new InMemoryCtdNodeRepository(revisions, placements);
        CtdSequenceWorkspace? workspace = null;
        if (withNodes)
        {
            var graph = new CtdNodeGraph(application.Id, IchSectionDefinitions.Current);
            var module = graph.Create("m3-quality", null, new Dictionary<string, string>());
            var body = graph.Create("m3-2-body-of-data", module.Id, new Dictionary<string, string>());
            var segments = new Dictionary<Guid, string>();
            foreach (var (manufacturer, segment) in new[] { ("Alpha", "drug-a-alpha"), ("Beta", "drug-a-beta") })
            {
                var substance = graph.Create("m3-2-s-drug-substance", body.Id, new Dictionary<string, string> { ["substance"] = "Drug A", ["manufacturer"] = manufacturer });
                segments[substance.Id] = segment;
                var control = graph.Create("m3-2-s-4-control-of-drug-substance", substance.Id, new Dictionary<string, string>());
                graph.Create("m3-2-s-4-1-specification", control.Id, new Dictionary<string, string>());
            }
            var sequenceNodes = graph.Nodes.Values.Select(node => new SequenceNode(graph, "0001", node.Id, node.IdentityAttributes,
                storageSegment: segments.GetValueOrDefault(node.Id))).ToArray();
            await nodeRepository.SaveSequenceAsync(graph, "0001", sequenceNodes, 0);
            workspace = (await nodeRepository.GetSequenceAsync(application.Id, "0001"))!;
        }
        var paths = new CtdNodePathResolver(new EctdWorkspacePathResolver());
        var engine = new QpdfProcessingEngine(new QpdfProcessingOptions { ExecutablePath = Environment.GetEnvironmentVariable("RATOOLS_QPDF") ??
            Path.GetFullPath(".artifacts/tools/qpdf-12.4.1/qpdf-12.4.1-msvc64/bin/qpdf.exe") });
        var inspector = new PdfPigPackageInspector();
        var previews = new PdfRenamePreviewService(applications, documents, placements, nodeRepository, paths, mutation, files, versions, engine, inspector, options);
        var service = new PdfRenameService(previews, store, versions, applications, documents, placements, nodeRepository, paths, mutation, files, engine, inspector, options);
        var guard = new PdfRenameImpactGuard(applications, documents, placements, new DocumentStorageBoundary(pathPolicy), inspector, options);
        var metadata = new DocumentPlacementService(placements, documents, null!, applications, null!, null!, new DocumentStorageBoundary(pathPolicy),
            mutation, renameImpact: guard);
        var nodeMoves = new CtdNodePlacementService(nodeRepository, placements, documents, applications, new DocumentStorageBoundary(pathPolicy),
            paths, null!, mutation, new NodeFileMoveJournal(pathPolicy), guard);
        var context = new Context(application, documents, placements, store, versions, revisions, engine, service, [], guard, metadata, nodeMoves, workspace);
        return context with { Mounts = [await AddAsync(context, "source.pdf"), await AddAsync(context, "old.pdf")] };
    }

    private static async Task<DocumentPlacement> AddAsync(Context test, string name)
    {
        var bytes = await File.ReadAllBytesAsync("tests/RATools.Tests/Fixtures/PdfProcessing/navigation-source.pdf");
        var path = Path.Combine(test.Application.WorkingDirectoryPath, "0001", "m5", name);
        await File.WriteAllBytesAsync(path, bytes);
        var document = new SubmissionDocument(name, "application/pdf", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), path);
        await test.Documents.AddAsync(document);
        var placement = new DocumentPlacement(document.Id, test.Application.Id, "0001", "m5.3.1", DocumentPlacementOperation.New, name);
        await test.Placements.AddAsync(placement); return placement;
    }
    private sealed record Context(SubmissionApplication Application, InMemoryDocumentRepository Documents, Placements Placements,
        InMemoryPdfRenameStore Store, InMemoryDocumentProcessingStore Versions, InMemoryWorkspaceRevisionStore Revisions,
        QpdfProcessingEngine Engine, PdfRenameService Service, DocumentPlacement[] Mounts, PdfRenameImpactGuard Guard,
        DocumentPlacementService Metadata, CtdNodePlacementService? NodeMoves, CtdSequenceWorkspace? Workspace);
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
    private sealed class Placements : IDocumentPlacementRepository
    {
        private readonly InMemoryDocumentPlacementRepository _inner = new();
        private int _updates;
        public bool FailSecondUpdate { get; set; }
        public Task AddAsync(DocumentPlacement item, CancellationToken cancellationToken = default) => _inner.AddAsync(item, cancellationToken);
        public Task<bool> UpdateAsync(DocumentPlacement item, CancellationToken cancellationToken = default)
        {
            if (FailSecondUpdate && ++_updates == 2) throw new InvalidOperationException("Injected second mount failure.");
            return _inner.UpdateAsync(item, cancellationToken);
        }
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default) => _inner.GetAsync(id, cancellationToken);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default) => _inner.ListAsync(cancellationToken);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid id, CancellationToken cancellationToken = default) => _inner.ListByApplicationAsync(id, cancellationToken);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid id, string sequence, CancellationToken cancellationToken = default) => _inner.ListBySequenceAsync(id, sequence, cancellationToken);
    }
}
