using RATools.Application.Assembly;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Finalization;
using RATools.Application.PackageValidation;
using RATools.Domain.Documents;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Assembly;

// Planner coverage uses safe real sources; all task evidence is retained.
public sealed class AssemblyPlannerTests
{
    [Theory]
    [InlineData(DocumentPlacementOperation.Replace)]
    [InlineData(DocumentPlacementOperation.Delete)]
    public async Task AnEarlierReplaceOrDeleteMakesTheExactHistoricalTargetIneffective(DocumentPlacementOperation operation)
    {
        var rig = await HistoryRigAsync();
        var original = await AddHistoryAsync(rig, "0000", DocumentPlacementOperation.New);
        var successor = await AddHistoryAsync(rig, "0001", operation, original.Id);
        var revision = (await rig.Revisions.GetRevisionAsync(rig.Application.Id, "0003"))!.Value;
        var plan = await Planner(rig).PlanAsync(rig.Application.Id, "0003", revision,
            $"documentId,nodeInstanceId,operation,targetLeafAddress\n,{rig.Alpha},delete,{original.Id}");

        Assert.Equal(0, plan.ValidRows);
        Assert.Contains(Assert.Single(plan.Rows).Diagnostics, issue => issue.Code == "ASSEMBLY_TARGET_NOT_EFFECTIVE");
        if (operation == DocumentPlacementOperation.Replace)
        {
            var current = await Planner(rig).PlanAsync(rig.Application.Id, "0003", revision,
                $"documentId,nodeInstanceId,operation,targetLeafAddress\n,{rig.Alpha},delete,{successor.Id}");
            Assert.Equal(1, current.ValidRows);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnAppendBranchRemainsUnqualifiedInsteadOfBeingAssumedCurrentOrDeleted(bool useAppendLeaf)
    {
        var rig = await HistoryRigAsync();
        var original = await AddHistoryAsync(rig, "0000", DocumentPlacementOperation.New);
        var appended = await AddHistoryAsync(rig, "0001", DocumentPlacementOperation.Append, original.Id);
        var revision = (await rig.Revisions.GetRevisionAsync(rig.Application.Id, "0003"))!.Value;
        var plan = await Planner(rig).PlanAsync(rig.Application.Id, "0003", revision,
            $"documentId,nodeInstanceId,operation,targetLeafAddress\n,{rig.Alpha},delete,{(useAppendLeaf ? appended.Id : original.Id)}");

        Assert.Equal(0, plan.ValidRows);
        Assert.Contains(Assert.Single(plan.Rows).Diagnostics, issue => issue.Code == "ASSEMBLY_TARGET_EFFECTIVENESS_UNKNOWN");
        Assert.DoesNotContain(Assert.Single(plan.Rows).Diagnostics, issue => issue.Code == "ASSEMBLY_TARGET_NOT_EFFECTIVE");
    }

    private static async Task<AssemblyRig> HistoryRigAsync()
    {
        var rig = await AssemblyRig.CreateAsync();
        var app = (await rig.Applications.GetAsync(rig.Application.Id))!;
        app.CreateSequence("0000", "original", "Original history");
        app.CreateSequence("0002", "amendment", "Intermediate history");
        app.CreateSequence("0003", "amendment", "History effectiveness");
        await rig.Applications.UpdateAsync(app);
        var workspace = (await rig.Nodes.GetSequenceAsync(app.Id, "0001"))!;
        await rig.Nodes.SaveSequenceAsync(workspace.Graph, "0003", workspace.Nodes.Select(node =>
            new RATools.Domain.Ctd.SequenceNode(workspace.Graph, "0003", node.NodeInstanceId, node.Attributes,
                null, node.SortOrder, node.StorageSegment)).ToArray(), 0);
        return rig;
    }

    [Fact]
    public async Task ATargetInvalidatedAfterPreviewCannotBeCommittedByTheDurableWorker()
    {
        var rig = await HistoryRigAsync();
        var directory = Path.Combine(rig.Root, "0000", "uploaded");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "original.txt");
        await File.WriteAllTextAsync(path, "original");
        var bytes = await File.ReadAllBytesAsync(path);
        var document = new SubmissionDocument("original.txt", "text/plain", bytes.Length,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
            Convert.ToHexString(System.Security.Cryptography.MD5.HashData(bytes)).ToLowerInvariant(), path);
        await rig.Documents.AddAsync(document);
        var original = DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, rig.Application.Id, "0000", "m3.2.s.4.1",
            DocumentPlacementOperation.New, "Original", null, DateTime.UtcNow, nodeInstanceId: rig.Alpha);
        await rig.Placements.AddAsync(original);
        var revision = (await rig.Revisions.GetRevisionAsync(rig.Application.Id, "0003"))!.Value;
        var preview = await rig.Service.PreviewAsync(rig.Application.Id, "0003", new(revision,
            $"documentId,nodeInstanceId,operation,targetLeafAddress\n,{rig.Alpha},delete,{original.Id}"));
        Assert.True(preview.CanExecute);
        var queued = await rig.Service.EnqueueAsync(rig.Application.Id, "0003", new(preview.Id, preview.Plan.PlanDigest, "history-change"));
        var lease = (await rig.Store.ClaimAsync("history-change", DateTime.UtcNow, TimeSpan.FromMinutes(1)))!;
        await rig.Placements.AddAsync(DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, rig.Application.Id, "0002", "m3.2.s.4.1",
            DocumentPlacementOperation.Replace, "Replacement", original.Id, DateTime.UtcNow, nodeInstanceId: rig.Alpha));

        await Assert.ThrowsAsync<AssemblyPlanStaleException>(() => rig.Service.ExecuteAsync(lease));
        Assert.Empty(await rig.Placements.ListBySequenceAsync(rig.Application.Id, "0003"));
        Assert.Equal(revision, await rig.Revisions.GetRevisionAsync(rig.Application.Id, "0003"));
        Assert.Null((await rig.Store.GetJobAsync(queued.Job.State.Id))!.Result);
    }

    [Fact]
    public async Task SameSequenceHistoricalOperationsDoNotSilentlyEstablishEffectiveness()
    {
        var rig = await HistoryRigAsync();
        var original = await AddHistoryAsync(rig, "0000", DocumentPlacementOperation.New);
        await AddHistoryAsync(rig, "0000", DocumentPlacementOperation.Replace, original.Id);
        var revision = (await rig.Revisions.GetRevisionAsync(rig.Application.Id, "0003"))!.Value;
        var plan = await Planner(rig).PlanAsync(rig.Application.Id, "0003", revision,
            $"documentId,nodeInstanceId,operation,targetLeafAddress\n,{rig.Alpha},delete,{original.Id}");

        Assert.Contains(Assert.Single(plan.Rows).Diagnostics, issue => issue.Code == "ASSEMBLY_TARGET_EFFECTIVENESS_UNKNOWN");
    }

    [Fact]
    public async Task ACrossNodeHistoricalReplacementCannotProveTheTargetWasReplaced()
    {
        var rig = await HistoryRigAsync();
        var original = await AddHistoryAsync(rig, "0000", DocumentPlacementOperation.New);
        await rig.Placements.AddAsync(DocumentPlacement.Rehydrate(Guid.NewGuid(), rig.Document.Id, rig.Application.Id,
            "0001", "m3.2.s.4.1", DocumentPlacementOperation.Replace, "Wrong node", original.Id, DateTime.UtcNow, nodeInstanceId: rig.Beta));
        var revision = (await rig.Revisions.GetRevisionAsync(rig.Application.Id, "0003"))!.Value;
        var plan = await Planner(rig).PlanAsync(rig.Application.Id, "0003", revision,
            $"documentId,nodeInstanceId,operation,targetLeafAddress\n,{rig.Alpha},delete,{original.Id}");

        Assert.Contains(Assert.Single(plan.Rows).Diagnostics, issue => issue.Code == "ASSEMBLY_TARGET_EFFECTIVENESS_UNKNOWN");
        Assert.DoesNotContain(Assert.Single(plan.Rows).Diagnostics, issue => issue.Code == "ASSEMBLY_TARGET_NOT_EFFECTIVE");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnUnresolvedHistoricalOperationKeepsEarlierTargetsUnknownButDoesNotInvalidateLaterNewLeaves(bool missingPlacement)
    {
        var rig = await HistoryRigAsync();
        var original = await AddHistoryAsync(rig, "0000", DocumentPlacementOperation.New);
        await AddHistoryAsync(rig, "0001", DocumentPlacementOperation.Replace, missingPlacement ? Guid.NewGuid() : null);
        var later = await AddHistoryAsync(rig, "0002", DocumentPlacementOperation.New);
        var revision = (await rig.Revisions.GetRevisionAsync(rig.Application.Id, "0003"))!.Value;
        var plan = await Planner(rig).PlanAsync(rig.Application.Id, "0003", revision,
            $"documentId,nodeInstanceId,operation,targetLeafAddress\n,{rig.Alpha},delete,{original.Id}\n,{rig.Alpha},delete,{later.Id}");

        Assert.Contains(plan.Rows[0].Diagnostics, issue => issue.Code == "ASSEMBLY_TARGET_EFFECTIVENESS_UNKNOWN");
        Assert.Equal(AssemblyRowStatus.Valid, plan.Rows[1].Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SameSequenceReplacementChainsRemainUnknownRegardlessOfRepositoryOrder(bool reverse)
    {
        var rig = await HistoryRigAsync();
        var original = await AddHistoryAsync(rig, "0000", DocumentPlacementOperation.New);
        var middle = DocumentPlacement.Rehydrate(Guid.NewGuid(), rig.Document.Id, rig.Application.Id, "0001", "m3.2.s.4.1",
            DocumentPlacementOperation.Replace, "Middle", original.Id, new DateTime(2026, 1, 1, 0, 0, reverse ? 2 : 1, DateTimeKind.Utc), nodeInstanceId: rig.Alpha);
        var last = DocumentPlacement.Rehydrate(Guid.NewGuid(), rig.Document.Id, rig.Application.Id, "0001", "m3.2.s.4.1",
            DocumentPlacementOperation.Replace, "Last", middle.Id, new DateTime(2026, 1, 1, 0, 0, reverse ? 1 : 2, DateTimeKind.Utc), nodeInstanceId: rig.Alpha);
        await rig.Placements.AddAsync(reverse ? last : middle);
        await rig.Placements.AddAsync(reverse ? middle : last);
        var revision = (await rig.Revisions.GetRevisionAsync(rig.Application.Id, "0003"))!.Value;
        var plan = await Planner(rig).PlanAsync(rig.Application.Id, "0003", revision,
            $"documentId,nodeInstanceId,operation,targetLeafAddress\n,{rig.Alpha},delete,{middle.Id}");

        Assert.Contains(Assert.Single(plan.Rows).Diagnostics, issue => issue.Code == "ASSEMBLY_TARGET_EFFECTIVENESS_UNKNOWN");
    }

    private static async Task<DocumentPlacement> AddHistoryAsync(AssemblyRig rig, string sequence,
        DocumentPlacementOperation operation, Guid? target = null)
    {
        var placement = DocumentPlacement.Rehydrate(Guid.NewGuid(), rig.Document.Id, rig.Application.Id, sequence,
            "m3.2.s.4.1", operation, "Historical", target, DateTime.UtcNow, nodeInstanceId: rig.Alpha);
        await rig.Placements.AddAsync(placement);
        return placement;
    }

    [Fact]
    public async Task ValidRowsResolveExplicitNodeAndDocumentAndInvalidRowsAreFullyReported()
    {
        var rig = await AssemblyRig.CreateAsync(); var planner = Planner(rig);
        var plan = await planner.PlanAsync(rig.Application.Id, "0001", rig.Revision,
            $"documentId,nodeInstanceId,operation,title,sortOrder\n{rig.Document.Id},{rig.Alpha},new,Alpha specification,2\n,{rig.Alpha},new,Missing document,1\n{rig.Document.Id},not-a-node,new,Bad node,3");
        Assert.Equal(3, plan.TotalRows); Assert.Equal(1, plan.ValidRows); Assert.Equal(2, plan.InvalidRows);
        Assert.Equal(rig.Alpha.ToString("D"), Assert.Single(plan.Rows, row => row.Status == AssemblyRowStatus.Valid).NodeInstanceId);
        Assert.Contains(plan.Rows, row => row.Diagnostics.Any(issue => issue.Code == "ASSEMBLY_DOCUMENT_REQUIRED"));
        Assert.Contains(plan.Rows, row => row.Diagnostics.Any(issue => issue.Code == "ASSEMBLY_NODE_REQUIRED"));
    }

    [Fact]
    public async Task LifecycleRowsRequireAnExplicitEarlierSequenceTargetOnTheSameNode()
    {
        var rig = await AssemblyRig.CreateAsync(); var planner = Planner(rig); var id = Guid.NewGuid();
        await rig.Placements.AddAsync(DocumentPlacement.Rehydrate(id, rig.Document.Id, rig.Application.Id, "0000", "m3.2.s.4.1", DocumentPlacementOperation.New,
            "Alpha", null, DateTime.UtcNow, nodeInstanceId: rig.Alpha));
        var plan = await planner.PlanAsync(rig.Application.Id, "0001", rig.Revision,
            $"documentId,nodeInstanceId,operation,targetLeafAddress\n{rig.Document.Id},{rig.Alpha},replace,{id}\n{rig.Document.Id},{rig.Alpha},replace,\n{rig.Document.Id},{rig.Alpha},replace,{Guid.NewGuid()}");
        Assert.Equal(1, plan.ValidRows);
        Assert.Contains(plan.Rows, row => row.Diagnostics.Any(issue => issue.Code == "ASSEMBLY_TARGET_REQUIRED"));
        Assert.Contains(plan.Rows, row => row.Diagnostics.Any(issue => issue.Code == "ASSEMBLY_TARGET_NOT_FOUND"));
        var current = Guid.NewGuid(); await rig.Placements.AddAsync(DocumentPlacement.Rehydrate(current, rig.Document.Id, rig.Application.Id,
            "0001", "m3.2.s.4.1", DocumentPlacementOperation.New, "Current", null, DateTime.UtcNow, nodeInstanceId: rig.Alpha));
        var same = await planner.PlanAsync(rig.Application.Id, "0001", rig.Revision, $"documentId,nodeInstanceId,operation,targetLeafAddress\n{rig.Document.Id},{rig.Alpha},replace,{current}");
        Assert.Contains(Assert.Single(same.Rows).Diagnostics, issue => issue.Code == "ASSEMBLY_TARGET_SAME_SEQUENCE");
    }

    [Fact]
    public async Task AChangedRevisionOrManifestMakesThePreviewStale()
    {
        var rig = await AssemblyRig.CreateAsync(); var planner = Planner(rig); var csv = rig.Manifest(1);
        var plan = await planner.PlanAsync(rig.Application.Id, "0001", rig.Revision, csv);
        var stale = await planner.PlanAsync(rig.Application.Id, "0001", rig.Revision + 1, csv);
        Assert.Empty(stale.PlanDigest); Assert.Contains(stale.Diagnostics, issue => issue.Code == "ASSEMBLY_REVISION_CONFLICT");
        var changed = await planner.PlanAsync(rig.Application.Id, "0001", rig.Revision, csv.Replace(",new,", ",append,", StringComparison.Ordinal));
        Assert.NotEqual(plan.PlanDigest, changed.PlanDigest);
    }

    [Fact]
    public async Task DurableExecutionCommitsEverySelectedRowAndAChangedRevisionIsRejected()
    {
        var rig = await AssemblyRig.CreateAsync();
        var plan = await rig.Service.PreviewAsync(rig.Application.Id, "0001", new(rig.Revision, rig.Manifest(2)));
        var queued = await rig.Service.EnqueueAsync(rig.Application.Id, "0001", new(plan.Id, plan.Plan.PlanDigest, "planner"));
        var lease = (await rig.Store.ClaimAsync("planner", DateTime.UtcNow, TimeSpan.FromMinutes(1)))!; await rig.Service.ExecuteAsync(lease);
        Assert.Equal(2, (await rig.Store.GetJobAsync(queued.Job.State.Id))!.Result!.Documents.Count);
        await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => rig.Service.PreviewAsync(rig.Application.Id, "0001", new(rig.Revision, rig.Manifest(1))));
    }

    [Fact]
    public async Task DurableExecutionRefusesInvalidRowsAndStaleRevisionWithoutMounting()
    {
        var rig = await AssemblyRig.CreateAsync(); var invalid = await rig.Service.PreviewAsync(rig.Application.Id, "0001", new(rig.Revision,
            $"documentId,nodeInstanceId,operation\n,{rig.Alpha},new"));
        await Assert.ThrowsAsync<AssemblyPlanInvalidException>(() => rig.Service.EnqueueAsync(rig.Application.Id, "0001", new(invalid.Id, invalid.Plan.PlanDigest, "invalid")));
        var valid = await rig.Service.PreviewAsync(rig.Application.Id, "0001", new(rig.Revision, rig.Manifest(1)));
        await rig.Revisions.AdvanceAsync(rig.Application.Id, "0001", rig.Revision);
        await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => rig.Service.EnqueueAsync(rig.Application.Id, "0001", new(valid.Id, valid.Plan.PlanDigest, "stale")));
        Assert.Empty(await rig.Placements.ListBySequenceAsync(rig.Application.Id, "0001"));
    }

    [Fact]
    public async Task FinalizedDraftRefusesIntakePreviewEnqueueAndWorkerWrites()
    {
        var rig = await AssemblyRig.CreateAsync(); var store = new InMemoryFinalizationStore(); var guard = new FinalizedWorkspaceGuard(store);
        var service = rig.MakeService(rig.Placements, finalizations: guard);
        var plan = await service.PreviewAsync(rig.Application.Id, "0001", new(rig.Revision, rig.Manifest(1)));
        var queued = await service.EnqueueAsync(rig.Application.Id, "0001", new(plan.Id, plan.Plan.PlanDigest, "freeze"));
        var lease = (await rig.Store.ClaimAsync("freeze", DateTime.UtcNow, TimeSpan.FromMinutes(1)))!;
        await store.RegisterAsync(FinalizedSequence.Create(rig.Application.Id, "0001", rig.Revision, new string('c', 64), PackageValidationCatalog.UsProfile,
            "3.2.2", null, new string('d', 64), Guid.NewGuid(), new string('e', 64), new string('f', 64), PackageValidationMode.Development, false, [],
            FinalizationService.ToolVersion, "assembly-boundary", DateTime.UtcNow));
        await Assert.ThrowsAsync<WorkspaceFinalizedException>(() => service.UploadSourceAsync(rig.Application.Id, "0001", rig.Revision, "source.pdf", new MemoryStream(rig.Original)));
        await Assert.ThrowsAsync<WorkspaceFinalizedException>(() => service.PreviewAsync(rig.Application.Id, "0001", new(rig.Revision, rig.Manifest(1))));
        await Assert.ThrowsAsync<WorkspaceFinalizedException>(() => service.SelectAsync(rig.Application.Id, "0001", plan.Id, new(rig.Revision, plan.Plan.PlanDigest, [2])));
        // Existing request replay is read-only; a worker still cannot commit it.
        Assert.Equal(queued.Job.State.Id, (await service.EnqueueAsync(rig.Application.Id, "0001", new(plan.Id, plan.Plan.PlanDigest, "freeze"))).Job.State.Id);
        await Assert.ThrowsAsync<WorkspaceFinalizedException>(() => service.ExecuteAsync(lease));
        Assert.Equal(rig.Revision, await rig.Revisions.GetRevisionAsync(rig.Application.Id, "0001")); Assert.Empty(await rig.Placements.ListBySequenceAsync(rig.Application.Id, "0001"));
    }

    private static AssemblyPlanner Planner(AssemblyRig rig) => new(rig.Applications, rig.Documents, rig.Placements, rig.Nodes,
        new RATools.Application.Documents.DocumentStorageBoundary(new AssemblyRig.Paths(rig.Root)), rig.Revisions);
}
