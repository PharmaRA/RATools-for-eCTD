using RATools.Application.Assembly;
using RATools.Application.Documents;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Applications;
using RATools.Domain.Ctd;
using RATools.Domain.Documents;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Application.Persistence;
using RATools.Application.Ctd;
using RATools.Tests.Applications;
using RATools.Tests.Documents;

namespace RATools.Tests.Assembly;

public sealed class AssemblyPlannerTests
{
    [Fact]
    public async Task ValidRowsResolveExplicitNodeAndDocumentAndInvalidRowsAreFullyReported()
    {
        await using var fixture = await Fixture.CreateAsync();
        var csv = $"""
            documentId,nodeInstanceId,operation,title,sortOrder
            {fixture.DocumentId},{fixture.LeafNodeId},new,Alpha specification,2
            ,{fixture.LeafNodeId},new,Missing document,1
            {fixture.DocumentId},not-a-node,new,Bad node,3
            """;
        var plan = await fixture.Planner.PlanAsync(fixture.ApplicationId, "0001", fixture.Revision, csv);
        Assert.Equal(3, plan.TotalRows);
        Assert.Equal(1, plan.ValidRows);
        Assert.Equal(2, plan.InvalidRows);
        var valid = Assert.Single(plan.Rows, row => row.Status == AssemblyRowStatus.Valid);
        Assert.Equal(fixture.LeafNodeId.ToString("D"), valid.NodeInstanceId);
        Assert.Contains(plan.Rows, row => row.Diagnostics.Any(diagnostic => diagnostic.Code == "ASSEMBLY_DOCUMENT_REQUIRED"));
        Assert.Contains(plan.Rows, row => row.Diagnostics.Any(diagnostic => diagnostic.Code == "ASSEMBLY_NODE_REQUIRED"));
    }

    [Fact]
    public async Task LifecycleRowsRequireAnExplicitEarlierSequenceTargetOnTheSameNode()
    {
        await using var fixture = await Fixture.CreateAsync();
        var historicalId = Guid.NewGuid();
        await fixture.Placements.AddAsync(DocumentPlacement.Rehydrate(historicalId, fixture.DocumentId, fixture.ApplicationId,
            "0000", Fixture.Section, DocumentPlacementOperation.New, "Alpha", null, DateTime.UtcNow,
            nodeInstanceId: fixture.LeafNodeId));
        var csv = $"""
            documentId,nodeInstanceId,operation,targetLeafAddress
            {fixture.DocumentId},{fixture.LeafNodeId},replace,{historicalId}
            {fixture.DocumentId},{fixture.LeafNodeId},replace,
            {fixture.DocumentId},{fixture.LeafNodeId},replace,{Guid.NewGuid()}
            """;
        var plan = await fixture.Planner.PlanAsync(fixture.ApplicationId, "0001", fixture.Revision, csv);
        Assert.Equal(1, plan.ValidRows);
        Assert.Contains(plan.Rows, row => row.Diagnostics.Any(diagnostic => diagnostic.Code == "ASSEMBLY_TARGET_REQUIRED"));
        Assert.Contains(plan.Rows, row => row.Diagnostics.Any(diagnostic => diagnostic.Code == "ASSEMBLY_TARGET_NOT_FOUND"));
        // A same-sequence target requires explicit policy and is refused rather than
        // resolved by file name.
        var currentTarget = Guid.NewGuid();
        await fixture.Placements.AddAsync(DocumentPlacement.Rehydrate(currentTarget, fixture.DocumentId, fixture.ApplicationId,
            "0001", Fixture.Section, DocumentPlacementOperation.New, "Current", null, DateTime.UtcNow,
            nodeInstanceId: fixture.LeafNodeId));
        var sameSequence = await fixture.Planner.PlanAsync(fixture.ApplicationId, "0001", fixture.Revision,
            $"documentId,nodeInstanceId,operation,targetLeafAddress\n{fixture.DocumentId},{fixture.LeafNodeId},replace,{currentTarget}");
        Assert.Contains(sameSequence.Rows[0].Diagnostics, diagnostic => diagnostic.Code == "ASSEMBLY_TARGET_SAME_SEQUENCE");
    }

    [Fact]
    public async Task AChangedRevisionOrManifestMakesThePreviewStale()
    {
        await using var fixture = await Fixture.CreateAsync();
        var csv = $"documentId,nodeInstanceId,operation\n{fixture.DocumentId},{fixture.LeafNodeId},new";
        var plan = await fixture.Planner.PlanAsync(fixture.ApplicationId, "0001", fixture.Revision, csv);
        var staleRevision = await fixture.Planner.PlanAsync(fixture.ApplicationId, "0001", fixture.Revision + 1, csv);
        Assert.Equal(string.Empty, staleRevision.PlanDigest);
        Assert.Contains(staleRevision.Diagnostics, diagnostic => diagnostic.Code == "ASSEMBLY_REVISION_CONFLICT");
        var changedManifest = await fixture.Planner.PlanAsync(fixture.ApplicationId, "0001", fixture.Revision,
            csv.Replace(",new", ",append", StringComparison.Ordinal));
        Assert.NotEqual(plan.PlanDigest, changedManifest.PlanDigest);
    }

    [Fact]
    public async Task ExecuteCommitsEverySelectedRowTogetherAndRefusesAStalePlan()
    {
        await using var fixture = await Fixture.CreateAsync();
        var second = await fixture.CreateDocumentAsync("second.pdf");
        var csv = $"""
            documentId,nodeInstanceId,operation,sortOrder
            {fixture.DocumentId},{fixture.LeafNodeId},new,1
            {second.Id},{fixture.LeafNodeId},new,2
            """;
        var plan = await fixture.Planner.PlanAsync(fixture.ApplicationId, "0001", fixture.Revision, csv);
        Assert.Equal(2, plan.ValidRows);
        await Assert.ThrowsAsync<AssemblyPlanStaleException>(() => fixture.Executor.ExecuteAsync(
            fixture.ApplicationId, "0001", fixture.Revision, new string('f', 64), csv));
        var executed = await fixture.Executor.ExecuteAsync(fixture.ApplicationId, "0001", fixture.Revision,
            plan.PlanDigest, csv);
        Assert.Equal(2, executed.PlacementIds.Count);
        Assert.Equal(fixture.Revision + 1, executed.WorkspaceRevision);
        var placements = await fixture.Placements.ListBySequenceAsync(fixture.ApplicationId, "0001");
        Assert.Equal(2, placements.Count);
        Assert.All(placements, placement => Assert.Equal(fixture.LeafNodeId, placement.NodeInstanceId));
        // The same execution cannot be replayed against the new revision.
        await Assert.ThrowsAsync<AssemblyPlanStaleException>(() => fixture.Executor.ExecuteAsync(
            fixture.ApplicationId, "0001", fixture.Revision, plan.PlanDigest, csv));
    }

    [Fact]
    public async Task ExecuteRefusesAPlanWithInvalidRowsAndAConcurrentRevisionChange()
    {
        await using var fixture = await Fixture.CreateAsync();
        var invalid = $"documentId,nodeInstanceId,operation\n,{fixture.LeafNodeId},new";
        var plan = await fixture.Planner.PlanAsync(fixture.ApplicationId, "0001", fixture.Revision, invalid);
        await Assert.ThrowsAsync<AssemblyPlanInvalidException>(() => fixture.Executor.ExecuteAsync(
            fixture.ApplicationId, "0001", fixture.Revision, plan.PlanDigest, invalid));

        var valid = $"documentId,nodeInstanceId,operation\n{fixture.DocumentId},{fixture.LeafNodeId},new";
        var validPlan = await fixture.Planner.PlanAsync(fixture.ApplicationId, "0001", fixture.Revision, valid);
        await fixture.Revisions.AdvanceAsync(fixture.ApplicationId, "0001", fixture.Revision);
        await Assert.ThrowsAsync<AssemblyPlanStaleException>(() => fixture.Executor.ExecuteAsync(
            fixture.ApplicationId, "0001", fixture.Revision, validPlan.PlanDigest, valid));
        Assert.Empty(await fixture.Placements.ListBySequenceAsync(fixture.ApplicationId, "0001"));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly NodeImportWorkspace _workspace = new();
        private readonly List<AssertingWorkspaceFile> _files = [];
        public Guid ApplicationId { get; private set; }
        public long Revision => _revision;
        private long _revision;
        public const string Section = "m3.2.s.4.1";
        public Guid LeafNodeId { get; private set; }
        public Guid DocumentId { get; private set; }
        public InMemoryDocumentRepository Documents => (InMemoryDocumentRepository)_workspace.Documents;
        public ImportFaultingPlacements Placements => _workspace.Placements;
        public IWorkspaceRevisionStore Revisions => _workspace.Revisions;
        public AssemblyPlanner Planner { get; }
        public AssemblyExecutor Executor { get; }

        private Fixture()
        {
            Planner = new AssemblyPlanner(_workspace.Applications, _workspace.Documents, _workspace.Placements,
                _workspace.Nodes, new PermissiveBoundary(), _workspace.Revisions);
            Executor = new AssemblyExecutor(Planner, _workspace.Placements, _workspace.Nodes, _workspace.Revisions,
                new PassthroughPersistenceTransaction());
        }

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture();
            var application = new SubmissionApplication($"APP-{Guid.NewGuid():N}", "US", "Sponsor",
                Path.Combine(Path.GetTempPath(), "assembly-" + Guid.NewGuid().ToString("N")), "us-fda-ectd-3.2.2");
            application.CreateSequence("0001", "original", "Assembly target");
            await fixture._workspace.Applications.AddAsync(application);
            fixture.ApplicationId = application.Id;
            var graph = new CtdNodeGraph(application.Id, IchSectionDefinitions.Current);
            var quality = graph.Create("m3-quality", null, new Dictionary<string, string>());
            var body = graph.Create("m3-2-body-of-data", quality.Id, new Dictionary<string, string>());
            var substance = graph.Create("m3-2-s-drug-substance", body.Id, new Dictionary<string, string>
            {
                ["substance"] = "Drug A", ["manufacturer"] = "Alpha"
            });
            var control = graph.Create("m3-2-s-4-control-of-drug-substance", substance.Id, new Dictionary<string, string>());
            var section = graph.Create("m3-2-s-4-1-specification", control.Id, new Dictionary<string, string>());
            var nodes = graph.Nodes.Values.Select(node => new SequenceNode(graph, "0001", node.Id, node.IdentityAttributes,
                node.Id == substance.Id ? "Alpha substance" : null, 0, node.Id == substance.Id ? "alpha-drug-a" : null)).ToArray();
            await fixture._workspace.Nodes.SaveSequenceAsync(graph, "0001", nodes, 0);
            fixture._revision = (await fixture._workspace.Revisions.GetRevisionAsync(application.Id, "0001"))!.Value;
            fixture.LeafNodeId = section.Id;
            fixture.DocumentId = (await fixture.CreateDocumentAsync("specification.pdf")).Id;
            return fixture;
        }

        public async Task<SubmissionDocument> CreateDocumentAsync(string name)
        {
            var directory = Path.Combine(Path.GetTempPath(), "assembly-files-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name);
            await File.WriteAllTextAsync(path, "pdf bytes " + name);
            var document = new SubmissionDocument(name, "application/pdf", new FileInfo(path).Length,
                new string('a', 64), new string('b', 32), path);
            await _workspace.Documents.AddAsync(document);
            _files.Add(new AssertingWorkspaceFile(directory));
            return document;
        }

        public async ValueTask DisposeAsync()
        {
            foreach (var file in _files) await file.DisposeAsync();
            _workspace.Dispose();
        }
    }

    private sealed class AssertingWorkspaceFile(string directory) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
            return ValueTask.CompletedTask;
        }
    }

    // The planner only asks the boundary whether a document belongs to the sequence; the
    // in-memory test documents already do.
    private sealed class PermissiveBoundary : IDocumentStorageBoundary
    {
        public string EnsureAllowedDocumentPath(SubmissionDocument document) => document.StoragePath;
        public string EnsureDocumentOwnedBySequence(SubmissionDocument document, SubmissionApplication application,
            string sequenceNumber) => document.StoragePath;
        public string EnsurePathOwnedBySequence(string path, SubmissionApplication application, string sequenceNumber) => path;
    }
}
