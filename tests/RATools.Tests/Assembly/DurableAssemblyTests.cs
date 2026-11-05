using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Abstractions.Security;
using RATools.Application.Assembly;
using RATools.Application.Ctd;
using RATools.Application.DocumentProcessing;
using RATools.Application.Documents;
using RATools.Application.PackageValidation;
using RATools.Application.Finalization;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Ctd;
using RATools.Domain.Documents;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Assembly;

public sealed class DurableAssemblyTests
{
    [Fact]
    public async Task AssemblySourceIntakeIsRecoverableWithoutMountingOrChangingGraphRevision()
    {
        var test = await AssemblyRig.CreateAsync();
        var upload = await test.Service.UploadSourceAsync(test.Application.Id, "0001", test.Revision, "received.pdf", new MemoryStream(test.Original));
        Assert.Equal("document:" + upload.DocumentId.ToString("D"), upload.SourceFile);
        Assert.Equal("received.pdf", upload.FileName); Assert.Equal(test.Original.Length, upload.Length);
        Assert.Equal(test.Document.Sha256, upload.Sha256); Assert.Equal(test.Document.Md5, upload.Md5);
        Assert.Equal(test.Revision, await test.Revisions.GetRevisionAsync(test.Application.Id, "0001"));
        Assert.Empty(await test.Placements.ListBySequenceAsync(test.Application.Id, "0001"));
        var receipt = Assert.Single(await test.Service.ListSourcesAsync(test.Application.Id, "0001"));
        Assert.Equal(upload.DocumentId, receipt.DocumentId);
        var stored = (await test.Documents.GetAsync(receipt.DocumentId))!;
        Assert.Contains("_assembly-sources", stored.StoragePath, StringComparison.Ordinal);
        Assert.Equal(test.Original, await File.ReadAllBytesAsync(stored.StoragePath));
        var preview = await test.Service.PreviewAsync(test.Application.Id, "0001", new(test.Revision,
            $"sourceFile,nodeInstanceId,fileName,operation\n{receipt.SourceFile},{test.Alpha},assembled.pdf,new"));
        Assert.True(preview.CanExecute); Assert.Equal(receipt.DocumentId, Assert.Single(preview.Sources).DocumentId);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => test.Service.ListSourcesAsync(Guid.NewGuid(), "0001"));
    }

    [Fact]
    public async Task SourceIntakeRejectsUnsafeNamesStaleRevisionAndStreamingOverLimitWithoutRecordingFailedSources()
    {
        var test = await AssemblyRig.CreateAsync();
        var service = test.MakeService(test.Placements, new AssemblyExecutionOptions { MaxFileBytes = 4 });
        await Assert.ThrowsAsync<ArgumentException>(() => service.UploadSourceAsync(test.Application.Id, "0001", test.Revision, "../private.pdf", new MemoryStream([1])));
        await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => service.UploadSourceAsync(test.Application.Id, "0001", test.Revision + 1, "source.pdf", new MemoryStream([1])));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.UploadSourceAsync(test.Application.Id, "0001", test.Revision, "source.pdf", new MemoryStream([1, 2, 3, 4, 5])));
        Assert.Empty(await service.ListSourcesAsync(test.Application.Id, "0001"));
        Assert.Single(await test.Documents.ListAsync());
        Assert.Equal(test.Revision, await test.Revisions.GetRevisionAsync(test.Application.Id, "0001"));
    }

    [Fact]
    public async Task ConflictingPlanAndKeyRowsReturnDefinedConflictInsteadOfAmbiguousLookupFailure()
    {
        var test = await AssemblyRig.CreateAsync();
        var first = await test.Service.PreviewAsync(test.Application.Id, "0001", new(test.Revision, test.Manifest(1)));
        var second = await test.Service.PreviewAsync(test.Application.Id, "0001", new(test.Revision, test.Manifest(2)));
        await test.Store.EnqueueAsync(AssemblyEvidence.Request(first, "plan-A"));
        await test.Store.EnqueueAsync(AssemblyEvidence.Request(second, "plan-B"));
        await Assert.ThrowsAsync<AssemblyPlanInvalidException>(() => test.Store.EnqueueAsync(AssemblyEvidence.Request(first, "plan-B")));
        Assert.False((await test.Store.EnqueueAsync(AssemblyEvidence.Request(first, "plan-A"))).Created);
    }

    [Fact]
    public async Task LegacyMetadataOnlyExecutorFailsClosedWithoutMountingUploads()
    {
        var test = await AssemblyRig.CreateAsync();
        var boundary = new DocumentStorageBoundary(new AssemblyRig.Paths(test.Root));
        var planner = new AssemblyPlanner(test.Applications, test.Documents, test.Placements, test.Nodes, boundary, test.Revisions);
        var plan = await planner.PlanAsync(test.Application.Id, "0001", test.Revision, test.Manifest(1));
        var executor = new AssemblyExecutor(planner, test.Placements, test.Nodes, test.Revisions, new InMemoryPersistenceTransaction());
        await Assert.ThrowsAsync<AssemblyPlanInvalidException>(() => executor.ExecuteAsync(test.Application.Id, "0001", test.Revision,
            plan.PlanDigest, test.Manifest(1)));
        Assert.Empty(await test.Placements.ListBySequenceAsync(test.Application.Id, "0001"));
    }

    [Fact]
    public async Task ExplicitSubsetGetsNewIdentityAndDigestThenCommitsOneRevisionAndReplaysAfterResponseLoss()
    {
        var test = await AssemblyRig.CreateAsync();
        var manifest = test.Manifest(3) + $"\ndocument:{Guid.NewGuid()},{test.Alpha},missing.pdf,new,Missing,99";
        var original = await test.Service.PreviewAsync(test.Application.Id, "0001", new(test.Revision, manifest));
        Assert.Equal(3, original.Plan.ValidRows); Assert.Equal(1, original.Plan.InvalidRows); Assert.False(original.CanExecute);
        Assert.False(original.SelectionConfirmed); original.Verify();
        var selected = await test.Service.SelectAsync(test.Application.Id, "0001", original.Id,
            new(test.Revision, original.Plan.PlanDigest, [2, 4]));
        Assert.NotEqual(original.Id, selected.Id); Assert.NotEqual(original.Plan.PlanDigest, selected.Plan.PlanDigest);
        Assert.True(selected.CanExecute); Assert.True(selected.SelectionConfirmed); Assert.Equal(1, selected.Plan.InvalidRows);
        var request = new AssemblyEnqueueRequest(selected.Id, selected.Plan.PlanDigest, "subset-" + selected.Id);
        var queued = await test.Service.EnqueueAsync(test.Application.Id, "0001", request);
        var lease = (await test.Store.ClaimAsync("test", DateTime.UtcNow, TimeSpan.FromMinutes(2)))!;
        await test.Service.ExecuteAsync(lease);
        var job = await test.Service.JobAsync(test.Application.Id, "0001", queued.Job.State.Id);
        Assert.Equal(PdfProcessingJobStatus.Completed, job.Status); Assert.Equal(2, job.Result!.Documents.Count);
        Assert.Equal(test.Revision + 1, job.Result.WorkspaceRevision);
        Assert.Equal(queued.Job.State.Id, (await test.Service.EnqueueAsync(test.Application.Id, "0001", request)).Job.State.Id);
        Assert.Equal(queued.Job.State.Id, (await test.Service.PlanJobAsync(test.Application.Id, "0001", selected.Id))!.Id);
        var mounted = await test.Placements.ListBySequenceAsync(test.Application.Id, "0001");
        Assert.Equal(2, mounted.Count); Assert.Equal(2, mounted.Select(item => item.LeafId).Distinct().Count());
        foreach (var mountedDocument in job.Result.Documents)
        {
            var document = (await test.Documents.GetAsync(mountedDocument.DocumentId))!;
            Assert.NotEqual(test.Document.Id, document.Id); Assert.Equal(test.Document.Sha256, document.Sha256);
            Assert.Contains("_assembled", document.StoragePath, StringComparison.Ordinal);
        }
        Assert.Equal(test.Original, await File.ReadAllBytesAsync(test.Document.StoragePath));
    }

    [Fact]
    public async Task ChangedSourceBytesAndInvalidSelectionAreRejectedBeforeEnqueue()
    {
        var test = await AssemblyRig.CreateAsync();
        var plan = await test.Service.PreviewAsync(test.Application.Id, "0001", new(test.Revision, test.Manifest(2)));
        Assert.True(plan.CanExecute);
        await Assert.ThrowsAsync<AssemblyPlanInvalidException>(() => test.Service.SelectAsync(test.Application.Id, "0001", plan.Id,
            new(test.Revision, plan.Plan.PlanDigest, [2, 999])));
        await File.AppendAllTextAsync(test.Document.StoragePath, "changed");
        await Assert.ThrowsAsync<InvalidDataException>(() => test.Service.EnqueueAsync(test.Application.Id, "0001", new(plan.Id, plan.Plan.PlanDigest, "changed")));
        Assert.Empty(await test.Placements.ListBySequenceAsync(test.Application.Id, "0001"));
    }

    [Fact]
    public async Task NewlySelectedSourceIsVerifiedEvenWhenOriginalInvalidPreviewHadNoSelection()
    {
        var test = await AssemblyRig.CreateAsync();
        var preview = await test.Service.PreviewAsync(test.Application.Id, "0001", new(test.Revision,
            test.Manifest(1) + $"\ndocument:{Guid.NewGuid()},{test.Beta},missing.pdf,new,Missing,5"));
        Assert.Empty(preview.SelectedRows);
        await File.AppendAllTextAsync(test.Document.StoragePath, "changed-before-selection");
        await Assert.ThrowsAsync<InvalidDataException>(() => test.Service.SelectAsync(test.Application.Id, "0001", preview.Id,
            new(test.Revision, preview.Plan.PlanDigest, [2])));
    }

    [Fact]
    public async Task AnExpiredParentCannotBeRevivedByConfirmingASelection()
    {
        var test = await AssemblyRig.CreateAsync();
        var service = test.MakeService(test.Placements, new AssemblyExecutionOptions { PlanLifetime = TimeSpan.FromSeconds(-1) });
        var preview = await service.PreviewAsync(test.Application.Id, "0001", new(test.Revision, test.Manifest(1)));
        await Assert.ThrowsAsync<AssemblyPlanStaleException>(() => test.Service.SelectAsync(test.Application.Id, "0001", preview.Id,
            new(test.Revision, preview.Plan.PlanDigest, [2])));
    }

    [Fact]
    public async Task ASubsetCanRemoveTheSourceOfAnUnresolvedLinkAndRevalidateItsOwnGraph()
    {
        var test = await AssemblyRig.CreateAsync(); var bytes = AssemblyRig.PdfBytes("missing.pdf");
        var path = Path.Combine(test.Root, "0001", "uploaded", "broken-link.pdf"); await File.WriteAllBytesAsync(path, bytes);
        var bad = new SubmissionDocument("broken-link.pdf", "application/pdf", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
            Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), path); await test.Documents.AddAsync(bad);
        var csv = $"sourceFile,nodeInstanceId,fileName,operation\ndocument:{test.Document.Id},{test.Alpha},valid.pdf,new\ndocument:{bad.Id},{test.Beta},broken-link.pdf,new";
        var preview = await test.Service.PreviewAsync(test.Application.Id, "0001", new(test.Revision, csv));
        Assert.False(preview.CanExecute); Assert.Contains(preview.Plan.Diagnostics, issue => issue.Code == "ASSEMBLY_LINK_INVALID");
        var subset = await test.Service.SelectAsync(test.Application.Id, "0001", preview.Id, new(test.Revision, preview.Plan.PlanDigest, [2]));
        Assert.True(subset.CanExecute); Assert.DoesNotContain(subset.Plan.Diagnostics, issue => issue.Code == "ASSEMBLY_LINK_INVALID");
        await test.Service.EnqueueAsync(test.Application.Id, "0001", new(subset.Id, subset.Plan.PlanDigest, "link-subset"));
        var lease = (await test.Store.ClaimAsync("link-subset", DateTime.UtcNow, TimeSpan.FromMinutes(1)))!; await test.Service.ExecuteAsync(lease);
        Assert.Single(await test.Placements.ListBySequenceAsync(test.Application.Id, "0001"));
    }

    [Fact]
    public async Task DeleteHistoricalTargetByteChangeIsRejectedEvenWithSameLengthAndUnchangedMetadata()
    {
        var test = await AssemblyRig.CreateAsync();
        var application = (await test.Applications.GetAsync(test.Application.Id))!;
        application.CreateSequence("0000", "original", "Historical target"); await test.Applications.UpdateAsync(application);
        var workspace = (await test.Nodes.GetSequenceAsync(test.Application.Id, "0001"))!;
        var historicalNodes = workspace.Nodes.Select(node => new SequenceNode(workspace.Graph, "0000", node.NodeInstanceId, node.Attributes,
            null, node.SortOrder, node.StorageSegment)).ToArray();
        await test.Nodes.SaveSequenceAsync(workspace.Graph, "0000", historicalNodes, 0);
        var historicalDirectory = Path.Combine(test.Root, "0000", "m3"); Directory.CreateDirectory(historicalDirectory);
        var path = Path.Combine(historicalDirectory, "historical.txt"); byte[] original = [1, 2, 3, 4]; await File.WriteAllBytesAsync(path, original);
        var document = new SubmissionDocument("historical.txt", "text/plain", 4, Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant(),
            Convert.ToHexString(MD5.HashData(original)).ToLowerInvariant(), path); await test.Documents.AddAsync(document);
        var target = new DocumentPlacement(document.Id, test.Application.Id, "0000", "m3.2.s.4.1", DocumentPlacementOperation.New, "Historical");
        target.BindToNode(historicalNodes.Single(node => node.NodeInstanceId == test.Alpha), 0); await test.Placements.AddAsync(target);
        var preview = await test.Service.PreviewAsync(test.Application.Id, "0001", new(test.Revision,
            $"sourceFile,nodeInstanceId,fileName,operation,targetLeafAddress\n,{test.Alpha},,delete,{target.Id}"));
        Assert.True(preview.CanExecute);
        await File.WriteAllBytesAsync(path, [4, 3, 2, 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => test.Service.EnqueueAsync(test.Application.Id, "0001",
            new(preview.Id, preview.Plan.PlanDigest, "historical-tamper")));
        Assert.Empty(await test.Placements.ListBySequenceAsync(test.Application.Id, "0001"));
    }

    [Fact]
    public async Task SecondPlacementFailureRollsBackDocumentsPlacementsRevisionAndExecutionResult()
    {
        var test = await AssemblyRig.CreateAsync();
        var failing = new FailingPlacements(test.Placements);
        var service = test.MakeService(failing);
        var plan = await service.PreviewAsync(test.Application.Id, "0001", new(test.Revision, test.Manifest(2)));
        var queued = await service.EnqueueAsync(test.Application.Id, "0001", new(plan.Id, plan.Plan.PlanDigest, "rollback"));
        var lease = (await test.Store.ClaimAsync("rollback", DateTime.UtcNow, TimeSpan.FromMinutes(2)))!;
        await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(lease));
        Assert.Empty(await test.Placements.ListBySequenceAsync(test.Application.Id, "0001"));
        Assert.Single(await test.Documents.ListAsync()); Assert.Equal(test.Revision, await test.Revisions.GetRevisionAsync(test.Application.Id, "0001"));
        Assert.Null((await test.Store.GetJobAsync(queued.Job.State.Id))!.Result);
    }

    [Fact]
    public async Task CancellationExpiredLeaseAndPartialResultsCannotCommit()
    {
        var test = await AssemblyRig.CreateAsync();
        var plan = await test.Service.PreviewAsync(test.Application.Id, "0001", new(test.Revision, test.Manifest(2)));
        var queued = await test.Service.EnqueueAsync(test.Application.Id, "0001", new(plan.Id, plan.Plan.PlanDigest, "lease"));
        var now = DateTime.UtcNow; var first = (await test.Store.ClaimAsync("old", now, TimeSpan.FromSeconds(1)))!;
        var next = (await test.Store.ClaimAsync("new", now.AddSeconds(2), TimeSpan.FromMinutes(1)))!;
        Assert.False(await test.Store.HeartbeatAsync(first.Job.Id, first.Token, first.Owner, now.AddSeconds(2), TimeSpan.FromMinutes(1)));
        await Assert.ThrowsAsync<InvalidDataException>(() => test.Store.CompleteAsync(next.Job.Id, next.Token, next.Owner, now.AddSeconds(2),
            new(next.Job.Id, plan.Id, plan.Plan.PlanDigest, test.Application.Id, "0001", test.Revision, test.Revision + 1, now, [])));
        await test.Service.CancelAsync(test.Application.Id, "0001", queued.Job.State.Id);
        await Assert.ThrowsAsync<AssemblyPlanStaleException>(() => test.Service.ExecuteAsync(next));
        Assert.Empty(await test.Placements.ListBySequenceAsync(test.Application.Id, "0001"));
        Assert.Equal(test.Original, await File.ReadAllBytesAsync(test.Document.StoragePath));
    }

    [Fact]
    public async Task SourceAliasConflictsFileNameCollisionsAndUnsupportedPathsAreVisibleBeforeExecution()
    {
        var test = await AssemblyRig.CreateAsync();
        var csv = "sourceFile,documentId,nodeInstanceId,fileName,operation" +
            $"\ndocument:{test.Document.Id},{Guid.NewGuid()},{test.Alpha},same.pdf,new" +
            $"\ndocument:{test.Document.Id},,{test.Alpha},same.pdf,new" +
            $"\ndocument:{test.Document.Id},,{test.Alpha},same.pdf,new" +
            $"\nC:/private.pdf,,{test.Alpha},bad.pdf,new";
        var plan = await test.Service.PreviewAsync(test.Application.Id, "0001", new(test.Revision, csv));
        Assert.Equal(1, plan.Plan.ValidRows); Assert.Equal(3, plan.Plan.InvalidRows);
        Assert.Contains(plan.Plan.Rows, row => row.Diagnostics.Any(issue => issue.Code == "ASSEMBLY_SOURCE_ALIAS_CONFLICT"));
        Assert.Contains(plan.Plan.Rows, row => row.Diagnostics.Any(issue => issue.Code == "ASSEMBLY_SOURCE_TOKEN_INVALID"));
        Assert.Contains(plan.Plan.Rows, row => row.Diagnostics.Any(issue => issue.Code == "ASSEMBLY_SOURCE_OR_PATH_INVALID"));
        Assert.False(plan.CanExecute);
    }

    private sealed class FailingPlacements(IDocumentPlacementRepository inner) : IDocumentPlacementRepository
    {
        private int _writes;
        public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => ++_writes == 2 ? Task.FromException(new IOException("Injected second row failure.")) : inner.AddAsync(placement, cancellationToken);
        public Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => inner.UpdateAsync(placement, cancellationToken);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No deletion in this test.");
        public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default) => inner.ListByApplicationAsync(applicationId, cancellationToken);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) => inner.ListBySequenceAsync(applicationId, sequenceNumber, cancellationToken);
    }
}

internal sealed class AssemblyRig
{
    public required string Root { get; init; }
    public required SubmissionApplication Application { get; init; }
    public required SubmissionDocument Document { get; init; }
    public required byte[] Original { get; init; }
    public required InMemoryApplicationRepository Applications { get; init; }
    public required InMemoryDocumentRepository Documents { get; init; }
    public required InMemoryDocumentPlacementRepository Placements { get; init; }
    public required InMemoryWorkspaceRevisionStore Revisions { get; init; }
    public required InMemoryCtdNodeRepository Nodes { get; init; }
    public required InMemoryDocumentProcessingStore Versions { get; init; }
    public required InMemoryAssemblyStore Store { get; init; }
    public required Guid Alpha { get; init; }
    public required Guid Beta { get; init; }
    public required long Revision { get; init; }
    public AssemblyService Service => MakeService(Placements);
    public string Manifest(int count) => "sourceFile,nodeInstanceId,fileName,operation,title,sortOrder" + string.Concat(Enumerable.Range(0, count)
        .Select(index => string.Create(CultureInfo.InvariantCulture, $"\ndocument:{Document.Id},{(index % 2 == 0 ? Alpha : Beta)},document-{index}.pdf,new,Document {index},{index / 2}")));
    public AssemblyService MakeService(IDocumentPlacementRepository placements, AssemblyExecutionOptions? executionOptions = null, IPackagePdfInspector? inspector = null,
        FinalizedWorkspaceGuard? finalizations = null)
    {
        var boundary = new DocumentStorageBoundary(new Paths(Root)); var options = executionOptions ?? new AssemblyExecutionOptions();
        var planner = new AssemblyPlanner(Applications, Documents, placements, Nodes, boundary, Revisions);
        return new(planner, Store, Applications, Documents, placements, Nodes, Versions, boundary,
            new CtdNodePathResolver(new RATools.Application.Validation.EctdWorkspacePathResolver()),
            new AssemblyFileStore(new Paths(Root), boundary, options), new WorkspaceMutationCoordinator(Revisions, new InMemoryPersistenceTransaction(), finalizedGuard: finalizations),
            inspector ?? new PdfPigPackageInspector(), options);
    }
    public static async Task<AssemblyRig> CreateAsync()
    {
        var root = Path.GetFullPath(".artifacts/p509-durable-tests/" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(Path.Combine(root, "0001", "uploaded"));
        var app = new SubmissionApplication("p509-" + Guid.NewGuid().ToString("N"), "US", "Synthetic", root, "us-fda-ectd-3.2.2"); app.CreateSequence("0001", "original", "Assembly");
        var applications = new InMemoryApplicationRepository(); await applications.AddAsync(app);
        var documents = new InMemoryDocumentRepository(); var placements = new InMemoryDocumentPlacementRepository(); var revisions = new InMemoryWorkspaceRevisionStore(applications);
        var nodes = new InMemoryCtdNodeRepository(revisions, placements); var graph = new CtdNodeGraph(app.Id, IchSectionDefinitions.Current);
        var quality = graph.Create("m3-quality", null, new Dictionary<string, string>()); var body = graph.Create("m3-2-body-of-data", quality.Id, new Dictionary<string, string>());
        Guid Manufacturer(string name)
        {
            var substance = graph.Create("m3-2-s-drug-substance", body.Id, new Dictionary<string, string> { ["substance"] = "Drug", ["manufacturer"] = name });
            var control = graph.Create("m3-2-s-4-control-of-drug-substance", substance.Id, new Dictionary<string, string>());
            return graph.Create("m3-2-s-4-1-specification", control.Id, new Dictionary<string, string>()).Id;
        }
        var alpha = Manufacturer("Alpha"); var beta = Manufacturer("Beta");
        var sequenceNodes = graph.Nodes.Values.Select(node => new SequenceNode(graph, "0001", node.Id, node.IdentityAttributes, null, 0,
            node.IdentityAttributes.TryGetValue("manufacturer", out var manufacturer) ? manufacturer.ToLowerInvariant() : null)).ToArray();
        var revision = await nodes.SaveSequenceAsync(graph, "0001", sequenceNodes, 0);
        var bytes = PdfBytes(); var path = Path.Combine(root, "0001", "uploaded", "source.pdf"); await File.WriteAllBytesAsync(path, bytes);
        var document = new SubmissionDocument("source.pdf", "application/pdf", bytes.Length, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant(), path);
        await documents.AddAsync(document);
        return new() { Root = root, Application = app, Document = document, Original = bytes, Applications = applications, Documents = documents,
            Placements = placements, Revisions = revisions, Nodes = nodes, Versions = new(), Store = new(), Alpha = alpha, Beta = beta, Revision = revision };
    }
    internal static byte[] PdfBytes(string? remoteFile = null)
    {
        List<string> objects = ["<</Type /Catalog /Pages 2 0 R>>", "<</Type /Pages /Kids [3 0 R] /Count 1>>", "<</Type /Page /Parent 2 0 R /MediaBox [0 0 400 220] /Resources <</Font <</F1 4 0 R>>>> /Contents 5 0 R" + (remoteFile is null ? "" : " /Annots [6 0 R]") + ">>", "<</Type /Font /Subtype /Type1 /BaseFont /Helvetica>>"];
        const string content = "BT /F1 18 Tf 30 100 Td (Clinical assembly source) Tj ET";
        objects.Add($"<</Length {content.Length}>>\nstream\n{content}\nendstream");
        if (remoteFile is not null) objects.Add($"<</Type /Annot /Subtype /Link /Rect [10 10 100 30] /A <</S /GoToR /F ({remoteFile}) /D [0 /Fit]>>>>");
        using var output = new MemoryStream(); void Write(string value) { var bytes = Encoding.ASCII.GetBytes(value); output.Write(bytes); }
        Write("%PDF-1.4\n"); var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++) { offsets.Add(output.Length); Write($"{index + 1} 0 obj\n" + objects[index] + "\nendobj\n"); }
        var xref = output.Length; Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"); foreach (var offset in offsets.Skip(1)) Write(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Write($"trailer\n<</Size {objects.Count + 1} /Root 1 0 R>>\nstartxref\n{xref}\n%%EOF\n"); return output.ToArray();
    }
    public sealed class Paths(string root) : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [root];
        public string EnsureAllowed(string path) { var full = Path.GetFullPath(path); if (full != root && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Outside assembly test workspace."); return full; }
    }
}
