using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Ctd;
using RATools.Application.RegionalMetadata;
using RATools.Application.Standards;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Ctd;
using RATools.Domain.Documents;
using RATools.Domain.RegionalMetadata;
using RATools.Infrastructure;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.RegionalMetadata;

public sealed class RegionalEvidenceRetentionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SupersededContextsRetainObjectsThroughTheRegisteredRepositories(bool removeNode)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Persistence:Provider"] = "InMemory" }).Build();
        using var provider = new ServiceCollection().AddInfrastructure(configuration).BuildServiceProvider();
        using var scope = provider.CreateScope(); var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IRegionalMetadataStore>();
        var references = services.GetRequiredService<IRegionalEvidenceReferences>();
        Assert.Same(store, references);
        var applications = services.GetRequiredService<IApplicationRepository>();
        var placements = services.GetRequiredService<IDocumentPlacementRepository>();
        var nodes = services.GetRequiredService<ICtdNodeRepository>();
        var revisions = services.GetRequiredService<IWorkspaceRevisionStore>();
        var transaction = services.GetRequiredService<IPersistenceTransaction>();
        var app = new SubmissionApplication("retain-" + Guid.NewGuid().ToString("N"), "EU", "Synthetic",
            Path.GetFullPath(".artifacts/regional-retention/" + Guid.NewGuid().ToString("N")), "eu-ectd-3.2.2");
        app.CreateSequence("0001", "initial", "Retention"); await applications.AddAsync(app);
        var graph = new CtdNodeGraph(app.Id, IchSectionDefinitions.Current);
        var node = graph.Create("m1-administrative-information-and-prescribing-information", null, new Dictionary<string, string>());
        var sequenceNode = new SequenceNode(graph, "0001", node.Id, node.IdentityAttributes);
        var revision = await nodes.SaveSequenceAsync(graph, "0001", [sequenceNode], 0);
        var placement = new DocumentPlacement(Guid.NewGuid(), app.Id, "0001", "m1", DocumentPlacementOperation.New, "Retained");
        placement.BindToNode(sequenceNode);
        if (!removeNode) await placements.AddAsync(placement);
        var snapshots = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new EuEctd322StandardsProfileProvider());
        var snapshot = await snapshots.CaptureAsync(app.EctdTemplateKey);
        var service = new RegionalMetadataService(store, applications, nodes, placements, snapshots,
            new WorkspaceMutationCoordinator(revisions, transaction), revisions, transaction);
        var content = new RegionalMetadataContent(1, "EU", Eu: new([]));
        var first = await service.SaveSequenceAsync(app.Id, "0001", revision, snapshot.Id, content,
            removeNode ? [new(node.Id, "de", null, null, null)] : [],
            removeNode ? [] : [new(placement.Id, node.Id, "fr", null, null, null)]);
        var current = await service.SaveSequenceAsync(app.Id, "0001", first.WorkspaceRevision, snapshot.Id, content, [], []);

        Assert.True(await references.HasNodesAsync(app.Id, "0001", [node.Id]));
        Assert.False(await references.HasNodesAsync(app.Id, "0000", [node.Id]));
        Assert.False(await references.HasNodesAsync(Guid.NewGuid(), "0001", [node.Id]));
        Assert.False(await references.HasNodesAsync(app.Id, "0001", []));
        if (removeNode)
            await Assert.ThrowsAsync<RegionalEvidenceReferencedException>(() => nodes.SaveSequenceAsync(graph, "0001", [], current.WorkspaceRevision!.Value));
        else
        {
            await Assert.ThrowsAsync<RegionalEvidenceReferencedException>(() => transaction.ExecuteAsync(ct => placements.DeleteAsync(placement.Id, ct)));
            Assert.NotNull(await placements.GetAsync(placement.Id));
        }
        Assert.Single((await nodes.GetSequenceAsync(app.Id, "0001"))!.Nodes);
        Assert.Equal(current.WorkspaceRevision, await revisions.GetRevisionAsync(app.Id, "0001"));
        Assert.Equal(first.Digest, (await store.GetRevisionAsync(first.Id))!.Digest);
        Assert.Equal(current.Id, (await store.GetHeadAsync(app.Id, "0001"))!.Id);
    }

    [Fact]
    public async Task ReferencesStagedInTheCurrentTransactionParticipateAndRollbackWithIt()
    {
        var store = new InMemoryRegionalMetadataStore(); var app = Guid.NewGuid(); var node = Guid.NewGuid(); var placement = Guid.NewGuid();
        var revision = RegionalMetadataRevision.Create(Guid.NewGuid(), app, "0001", 1, 1, "sha256:" + new string('a', 64),
            new(1, "EU", Eu: new([])), null, [], [new(placement, node, "fr", null, null, null)], null, null, [], [], null, null, DateTime.UtcNow);
        await Assert.ThrowsAsync<IOException>(() => new InMemoryPersistenceTransaction().ExecuteAsync(async ct =>
        {
            await store.AppendAsync(revision, null, ct);
            Assert.True(await store.HasPlacementAsync(placement, ct));
            Assert.True(await store.HasNodesAsync(app, "0001", [node], ct));
            throw new IOException("Rollback the staged evidence.");
        }));
        Assert.False(await store.HasPlacementAsync(placement));
        Assert.False(await store.HasNodesAsync(app, "0001", [node]));
        Assert.Null(await store.GetRevisionAsync(revision.Id));
    }
}
