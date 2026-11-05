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
