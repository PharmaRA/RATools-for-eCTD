using RATools.Application.Assembly;
using RATools.Application.Ctd;
using RATools.Application.Documents;
using RATools.Domain.Applications;
using RATools.Domain.Ctd;
using RATools.Domain.Documents;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Assembly;

public sealed class AssemblyDeleteTests
{
    [Fact]
    public async Task DeleteWithoutNewContentUsesTheExplicitHistoricalDocumentIdentity()
    {
        var applications = new InMemoryApplicationRepository();
        var placements = new InMemoryDocumentPlacementRepository();
        var documents = new InMemoryDocumentRepository();
        var revisions = new InMemoryWorkspaceRevisionStore(applications);
        var nodes = new InMemoryCtdNodeRepository(revisions, placements);
        var app = new SubmissionApplication("assembly-delete", "US", "Sponsor", Path.GetFullPath("memory-only"), "us-fda-ectd-3.2.2");
        app.CreateSequence("0000", "original", "History");
        app.CreateSequence("0001", "amendment", "Delete instruction");
        await applications.AddAsync(app);
        var graph = new CtdNodeGraph(app.Id, IchSectionDefinitions.Current);
        var quality = graph.Create("m3-quality", null, new Dictionary<string, string>());
        var body = graph.Create("m3-2-body-of-data", quality.Id, new Dictionary<string, string>());
        var substance = graph.Create("m3-2-s-drug-substance", body.Id,
            new Dictionary<string, string> { ["substance"] = "Drug A", ["manufacturer"] = "Alpha" });
        var control = graph.Create("m3-2-s-4-control-of-drug-substance", substance.Id, new Dictionary<string, string>());
        var section = graph.Create("m3-2-s-4-1-specification", control.Id, new Dictionary<string, string>());
        var sequenceNodes = graph.Nodes.Values.Select(node => new SequenceNode(graph, "0001", node.Id,
            node.IdentityAttributes, null, 0, node.Id == substance.Id ? "alpha-drug-a" : null)).ToArray();
        await nodes.SaveSequenceAsync(graph, "0001", sequenceNodes, 0);
        var revision = (await revisions.GetRevisionAsync(app.Id, "0001"))!.Value;
        var document = new SubmissionDocument("old.pdf", "application/pdf", 10, new string('a', 64), new string('b', 32),
            Path.GetFullPath("memory-only/0000/old.pdf"));
        await documents.AddAsync(document);
        var historical = DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, app.Id, "0000", "m3.2.s.4.1",
            DocumentPlacementOperation.New, "Historical", null, DateTime.UtcNow, nodeInstanceId: section.Id);
        await placements.AddAsync(historical);
        var planner = new AssemblyPlanner(applications, documents, placements, nodes, new UnusedBoundary(), revisions);
        var executor = new AssemblyExecutor(planner, placements, nodes, revisions, new InMemoryPersistenceTransaction());
        var csv = $"documentId,nodeInstanceId,operation,targetLeafAddress\n,{section.Id},delete,{historical.Id}";
        var plan = await planner.PlanAsync(app.Id, "0001", revision, csv);
        Assert.Equal(1, plan.ValidRows);
        var result = await executor.ExecuteAsync(app.Id, "0001", revision, plan.PlanDigest, csv);
        var instruction = Assert.Single(await placements.ListBySequenceAsync(app.Id, "0001"));
        Assert.Equal(DocumentPlacementOperation.Delete, instruction.Operation);
        Assert.Equal(document.Id, instruction.DocumentId);
        Assert.Equal(historical.Id, instruction.LifecycleTargetPlacementId);
        Assert.Equal(DocumentPlacementOperation.New, (await placements.GetAsync(historical.Id))!.Operation);
        Assert.Equal(revision + 1, result.WorkspaceRevision);
        Assert.Equal(revision + 1, await revisions.GetRevisionAsync(app.Id, "0001"));
        Assert.Single(await documents.ListAsync());
    }

    private sealed class UnusedBoundary : IDocumentStorageBoundary
    {
        public string EnsureAllowedDocumentPath(SubmissionDocument document) => throw new InvalidOperationException("Delete has no new payload.");
        public string EnsureDocumentOwnedBySequence(SubmissionDocument document, SubmissionApplication application, string sequenceNumber) =>
            throw new InvalidOperationException("Delete has no new payload.");
        public string EnsurePathOwnedBySequence(string path, SubmissionApplication application, string sequenceNumber) =>
            throw new InvalidOperationException("Delete has no new payload.");
    }
}
