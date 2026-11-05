using RATools.Application.Abstractions.Persistence;
using RATools.Application.Assembly;
using RATools.Application.DocumentProcessing;
using RATools.Domain.Documents;

namespace RATools.Tests.Assembly;

// All temporary source/capture/preparation evidence is retained; no delete cleanup.
public sealed class AssemblyWorkflowQualificationTests
{
    [Fact]
    public async Task ThousandRowsKeepTwoManufacturersSeparateAndAdvanceExactlyOneRevision()
    {
        var rig = await AssemblyRig.CreateAsync();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var preview = await rig.Service.PreviewAsync(rig.Application.Id, "0001", new(rig.Revision, rig.Manifest(1000)));
        Assert.Equal(1000, preview.Plan.TotalRows); Assert.Equal(1000, preview.Plan.ValidRows); Assert.True(preview.CanExecute);
        Assert.Equal(1000, preview.Sources.Count); Assert.Equal(500, preview.Plan.Rows.Count(row => row.NodeInstanceId == rig.Alpha.ToString("D")));
        Assert.Equal(500, preview.Plan.Rows.Count(row => row.NodeInstanceId == rig.Beta.ToString("D")));
        var request = new AssemblyEnqueueRequest(preview.Id, preview.Plan.PlanDigest, "scale-" + preview.Id);
        var queued = await rig.Service.EnqueueAsync(rig.Application.Id, "0001", request);
        var lease = (await rig.Store.ClaimAsync("scale", DateTime.UtcNow, TimeSpan.FromMinutes(5)))!;
        await rig.Service.ExecuteAsync(lease);
        var audit = (await rig.Store.GetJobAsync(queued.Job.State.Id))!.Result!;
        var mounted = await rig.Placements.ListBySequenceAsync(rig.Application.Id, "0001");
        Assert.Equal(1000, mounted.Count); Assert.Equal(1000, audit.Documents.Count);
        Assert.Equal(1000, mounted.Select(item => item.LeafId).Distinct().Count());
        Assert.Equal(1000, audit.Documents.Select(item => item.DocumentId).Distinct().Count());
        Assert.Equal(500, mounted.Count(item => item.NodeInstanceId == rig.Alpha)); Assert.Equal(500, mounted.Count(item => item.NodeInstanceId == rig.Beta));
        Assert.Equal(rig.Revision + 1, audit.WorkspaceRevision); Assert.Equal(rig.Revision + 1, await rig.Revisions.GetRevisionAsync(rig.Application.Id, "0001"));
        Assert.Equal(queued.Job.State.Id, (await rig.Service.EnqueueAsync(rig.Application.Id, "0001", request)).Job.State.Id);
        Assert.Equal(rig.Original, await File.ReadAllBytesAsync(rig.Document.StoragePath));
        Console.WriteLine($"ASSEMBLY_SCALE rows=1000 manufacturers=2 elapsedMs={watch.ElapsedMilliseconds} managedBytes={GC.GetTotalMemory(false)}");
    }

    [Fact]
    public async Task FailureAtRow999RollsBackDocumentsPlacementsAuditAndRevision()
    {
        var rig = await AssemblyRig.CreateAsync(); var failing = new FailAtPlacement(rig.Placements, 999); var service = rig.MakeService(failing);
        var preview = await service.PreviewAsync(rig.Application.Id, "0001", new(rig.Revision, rig.Manifest(1000)));
        var queued = await service.EnqueueAsync(rig.Application.Id, "0001", new(preview.Id, preview.Plan.PlanDigest, "row999-" + preview.Id));
        var lease = (await rig.Store.ClaimAsync("row999", DateTime.UtcNow, TimeSpan.FromMinutes(5)))!;
        await Assert.ThrowsAsync<IOException>(() => service.ExecuteAsync(lease));
        Assert.Equal(999, failing.Attempts); Assert.Empty(await rig.Placements.ListBySequenceAsync(rig.Application.Id, "0001"));
        Assert.Single(await rig.Documents.ListAsync()); Assert.Null((await rig.Store.GetJobAsync(queued.Job.State.Id))!.Result);
        Assert.Equal(rig.Revision, await rig.Revisions.GetRevisionAsync(rig.Application.Id, "0001"));
        await rig.Service.ExecuteAsync(lease);
        Assert.Equal(1000, (await rig.Store.GetJobAsync(queued.Job.State.Id))!.Result!.Documents.Count);
        Assert.Equal(rig.Original, await File.ReadAllBytesAsync(rig.Document.StoragePath));
    }

    [Fact]
    public async Task InvalidRowsStayVisibleWhileAnExplicitSubsetGetsItsOwnStoredIdentity()
    {
        var rig = await AssemblyRig.CreateAsync();
        var preview = await rig.Service.PreviewAsync(rig.Application.Id, "0001", new(rig.Revision,
            rig.Manifest(4) + $"\n,{rig.Alpha},missing.pdf,new,Missing,99"));
        Assert.Equal(4, preview.Plan.ValidRows); Assert.Equal(1, preview.Plan.InvalidRows);
        await Assert.ThrowsAsync<AssemblyPlanInvalidException>(() => rig.Service.EnqueueAsync(rig.Application.Id, "0001", new(preview.Id, preview.Plan.PlanDigest, "invalid")));
        var subset = await rig.Service.SelectAsync(rig.Application.Id, "0001", preview.Id, new(rig.Revision, preview.Plan.PlanDigest, [2, 3, 4, 5]));
        Assert.NotEqual(preview.Id, subset.Id); Assert.NotEqual(preview.Plan.PlanDigest, subset.Plan.PlanDigest); Assert.Equal(1, subset.Plan.InvalidRows);
        await rig.Service.EnqueueAsync(rig.Application.Id, "0001", new(subset.Id, subset.Plan.PlanDigest, "subset"));
        var lease = (await rig.Store.ClaimAsync("subset", DateTime.UtcNow, TimeSpan.FromMinutes(2)))!; await rig.Service.ExecuteAsync(lease);
        Assert.Equal(4, (await rig.Store.GetPlanJobAsync(subset.Id))!.Result!.Documents.Count);
    }

    private sealed class FailAtPlacement(IDocumentPlacementRepository inner, int failAt) : IDocumentPlacementRepository
    {
        public int Attempts { get; private set; }
        public Task AddAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => ++Attempts == failAt
            ? Task.FromException(new IOException("Injected row-999 persistence failure.")) : inner.AddAsync(placement, cancellationToken);
        public Task<bool> UpdateAsync(DocumentPlacement placement, CancellationToken cancellationToken = default) => inner.UpdateAsync(placement, cancellationToken);
        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new InvalidOperationException("No deletion in this qualification.");
        public Task<DocumentPlacement?> GetAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListByApplicationAsync(Guid app, CancellationToken cancellationToken = default) => inner.ListByApplicationAsync(app, cancellationToken);
        public Task<IReadOnlyCollection<DocumentPlacement>> ListBySequenceAsync(Guid app, string seq, CancellationToken cancellationToken = default) => inner.ListBySequenceAsync(app, seq, cancellationToken);
    }
}
