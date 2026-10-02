using RATools.Application.Abstractions.Persistence;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Standards;
using RATools.Domain.Ctd;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Tests.Applications;
using RATools.Tests.Publishing;
using RATools.Tests.TestDoubles;
using System.Xml.Linq;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresNodePublishingTests(PostgresFixture fixture)
{
    [RequiresPostgresFact]
    public async Task PersistentNodesProduceFourValidatedDeliveriesAndReimportWithTheirBusinessIdentity()
    {
        await using var database = fixture.CreateDbContext();
        using var source = new NodeImportWorkspace(database);
        source.CopyPublisherFixture();
        var indexPath = Path.Combine(source.Root, "0000", "index.xml");
        var input = EctdWorkspaceFixture.ReadXml(indexPath);
        input.Descendants("m3-quality").Single().SetAttributeValue(XNamespace.Xml + "lang", "en");
        input.Save(indexPath);
        var imported = await source.ImportAsync();
        database.ChangeTracker.Clear();
        using var restored = new NodeImportWorkspace();
        var deliveries = await NodePublishingTests.PublishFixtureAsync(source, restored.Root, imported.ApplicationId);
        await MultiInstanceRoundTripTests.InspectDeliveriesAsync(restored.Root, imported.ApplicationId, deliveries);
        var result = await restored.ImportAsync();
        Assert.Equal(4, result.ImportedSequenceCount);
        await ApplicationImportNodeTests.AssertFixtureAsync(restored, result.ApplicationId);
        var restoredNodes = (await restored.Nodes.GetSequenceAsync(result.ApplicationId, "0000"))!;
        Assert.Equal("en", restoredNodes.Nodes.Single(node => node.CtdSection == "m3").Attributes["xml:lang"]);
    }

    [RequiresPostgresFact]
    public async Task PackageReadHoldsTheApplicationLockAndCapturesOneNodeRevision()
    {
        await using var database = fixture.CreateDbContext();
        using var source = new NodeImportWorkspace(database);
        source.CopyPublisherFixture();
        var imported = await source.ImportAsync();
        var initial = (await source.Nodes.GetSequenceAsync(imported.ApplicationId, "0000"))!;
        var extension = initial.Nodes.Single(node => node.Title == "Study NC-001");
        var pausedNodes = new PausingNodes(source.Nodes);
        var builder = new EctdPackageModelBuilder(source.Applications, source.Placements, source.Documents,
            new FdaEctd322StandardsProfileProvider(), PermissiveDocumentStorageBoundary.Instance, pausedNodes, source.Revisions, source.Store);
        var build = builder.BuildAsync(new(imported.ApplicationId, "0000"));
        await pausedNodes.ReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await using var editDatabase = fixture.CreateDbContext();
        var revised = initial.Nodes.Select(node => node.NodeInstanceId == extension.NodeInstanceId
            ? new SequenceNode(initial.Graph, "0000", node.NodeInstanceId, node.Attributes, "Revised study title", node.SortOrder, node.StorageSegment) : node).ToArray();
        var edit = new EfCoreCtdNodeRepository(editDatabase).SaveSequenceAsync(initial.Graph, "0000", revised, initial.WorkspaceRevision);
        try { Assert.False(edit.IsCompleted); }
        finally { pausedNodes.Continue.TrySetResult(); }
        var originalPackage = await build.WaitAsync(TimeSpan.FromSeconds(10));
        await edit.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(1, originalPackage.WorkspaceRevision);
        Assert.Equal("Study NC-001", originalPackage.Nodes!.Single(node => node.NodeInstanceId == extension.NodeInstanceId).Title);
        var updatedPackage = await NodePublishingTests.Builder(source).BuildAsync(new(imported.ApplicationId, "0000"));
        Assert.Equal(2, updatedPackage.WorkspaceRevision);
        Assert.Equal("Revised study title", updatedPackage.Nodes!.Single(node => node.NodeInstanceId == extension.NodeInstanceId).Title);
    }

    private sealed class PausingNodes(ICtdNodeRepository inner) : ICtdNodeRepository
    {
        public TaskCompletionSource ReadStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<CtdSequenceWorkspace?> GetSequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
        {
            var result = await inner.GetSequenceAsync(applicationId, sequenceNumber, cancellationToken);
            ReadStarted.TrySetResult();
            await Continue.Task.WaitAsync(cancellationToken);
            return result;
        }
        public Task<long> SaveSequenceAsync(CtdNodeGraph graph, string sequenceNumber, IReadOnlyCollection<SequenceNode> nodes,
            long expectedRevision, CancellationToken cancellationToken = default) => inner.SaveSequenceAsync(graph, sequenceNumber, nodes, expectedRevision, cancellationToken);
    }
}
