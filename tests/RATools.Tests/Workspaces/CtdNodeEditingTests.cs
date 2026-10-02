using RATools.Application.Ctd;
using RATools.Application.Workspaces;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Ctd;

namespace RATools.Tests.Workspaces;

public sealed class CtdNodeEditingTests
{
    private static readonly string[] ConcurrentDefinitions = ["m2-common-technical-document-summaries", "m3-quality"];
    [Fact]
    public async Task CreateCloneInheritAndEditPreserveBusinessIdentityAndRevision()
    {
        using var workspace = new NodeMoveTestWorkspace();
        await ExerciseAsync(workspace);
    }

    internal static CtdNodeService Service(NodeMoveTestWorkspace workspace) => new(workspace.Nodes, workspace.Applications,
        workspace.Placements, workspace.Mutations, workspace.Revisions, workspace.Paths);

    internal static async Task ExerciseAsync(NodeMoveTestWorkspace workspace)
    {
        await workspace.Applications.AddAsync(workspace.Application);
        var service = Service(workspace);
        var appId = workspace.Application.Id;
        var initial = (await service.GetAsync(appId, "0000"))!;
        Assert.Empty(initial.Nodes);
        Assert.Equal(160, initial.Definitions.Count);
        long revision = 0;
        async Task<CtdNodeDto> Add(string key, Guid? parent, Dictionary<string, string>? attributes = null, string? segment = null)
        {
            var result = await service.CreateAsync(appId, "0000", new() { DefinitionKey = key, ParentInstanceId = parent,
                Attributes = attributes ?? [], StorageSegment = segment, ExpectedRevision = revision });
            revision = result.WorkspaceRevision;
            return result.Nodes.Single(node => node.DefinitionKey == key);
        }
        var root = await Add("m3-quality", null);
        var body = await Add("m3-2-body-of-data", root.NodeInstanceId);
        var alpha = await Add("m3-2-s-drug-substance", body.NodeInstanceId,
            new() { ["substance"] = "Drug A", ["manufacturer"] = "Alpha" }, "drug-a-alpha");
        var control = await Add("m3-2-s-4-control-of-drug-substance", alpha.NodeInstanceId);
        await Add("m3-2-s-4-1-specification", control.NodeInstanceId);
        var betaRequest = new UpdateCtdNodeRequest { ExpectedRevision = revision,
            Attributes = new() { ["substance"] = "Drug A", ["manufacturer"] = "Beta" }, StorageSegment = "drug-a-beta", Title = "Beta", SortOrder = 1 };
        var preview = await service.PreviewCloneAsync(appId, "0000", alpha.NodeInstanceId, betaRequest);
        Assert.Equal(3, preview.Nodes.Count);
        Assert.All(preview.Directories.Values, path => Assert.Contains("drug-a-beta", path));
        Assert.Equal(revision, await workspace.RevisionAsync());
        Assert.Equal(5, (await service.GetAsync(appId, "0000"))!.Nodes.Count);
        var result = await service.CloneAsync(appId, "0000", alpha.NodeInstanceId, betaRequest);
        revision = result.WorkspaceRevision;
        Assert.Equal(8, result.Nodes.Count);
        Assert.Empty(await workspace.Placements.ListByApplicationAsync(appId));
        var beta = result.Nodes.Single(node => node.Attributes.GetValueOrDefault("manufacturer") == "Beta");
        Assert.NotEqual(alpha.NodeInstanceId, beta.NodeInstanceId);
        Assert.Equal("NodeIdentityChangeRequiresNewInstance", (await Assert.ThrowsAsync<CtdNodeConstraintException>(() => service.UpdateAsync(appId, "0000", alpha.NodeInstanceId,
            new() { ExpectedRevision = revision, Attributes = betaRequest.Attributes, StorageSegment = alpha.StorageSegment }))).Code);
        Assert.Equal("NodeNotEmpty", (await Assert.ThrowsAsync<CtdNodeConstraintException>(() => service.DeleteAsync(appId, "0000", alpha.NodeInstanceId, revision))).Code);
        var conflict = new UpdateCtdNodeRequest { ExpectedRevision = revision,
            Attributes = new() { ["substance"] = "Drug A", ["manufacturer"] = "Gamma" }, StorageSegment = "drug-a-beta" };
        Assert.Equal("NodeDirectoryConflict", (await Assert.ThrowsAsync<CtdNodeConstraintException>(() => service.PreviewCloneAsync(appId, "0000", alpha.NodeInstanceId, conflict))).Code);
        Assert.Equal(revision, await workspace.RevisionAsync());

        var application = (await workspace.Applications.GetAsync(appId))!;
        application.CreateSequence("0001", "amendment", "Next sequence");
        await workspace.Applications.UpdateAsync(application);
        var inherited = await service.InheritAsync(appId, "0001", new("0000", 0));
        Assert.Equal(result.Nodes.Select(node => node.NodeInstanceId).Order(), inherited.Nodes.Select(node => node.NodeInstanceId).Order());
        var renamed = await service.UpdateAsync(appId, "0001", beta.NodeInstanceId, new() { ExpectedRevision = 1,
            Attributes = beta.Attributes.ToDictionary(), Title = "Beta in next sequence", SortOrder = 0, StorageSegment = beta.StorageSegment });
        Assert.Equal("Beta in next sequence", renamed.Nodes.Single(node => node.NodeInstanceId == beta.NodeInstanceId).Title);
        Assert.Equal("Beta", (await service.GetAsync(appId, "0000"))!.Nodes.Single(node => node.NodeInstanceId == beta.NodeInstanceId).Title);
        var leafNode = inherited.Nodes.Single(node => node.ParentInstanceId == inherited.Nodes.Single(parent =>
            parent.ParentInstanceId == beta.NodeInstanceId).NodeInstanceId);
        var removed = await service.DeleteAsync(appId, "0001", leafNode.NodeInstanceId, 2);
        Assert.DoesNotContain(removed.Nodes, node => node.NodeInstanceId == leafNode.NodeInstanceId);
        var readded = await service.CreateAsync(appId, "0001", new() { ExpectedRevision = 3, DefinitionKey = leafNode.DefinitionKey,
            ParentInstanceId = leafNode.ParentInstanceId });
        Assert.Contains(readded.Nodes, node => node.NodeInstanceId == leafNode.NodeInstanceId);
    }

    [Fact]
    public async Task MissingStaleAndConcurrentRevisionsDoNotWriteExtraNodes()
    {
        using var workspace = new NodeMoveTestWorkspace();
        await workspace.Applications.AddAsync(workspace.Application);
        var service = Service(workspace);
        var app = workspace.Application.Id;
        await Assert.ThrowsAsync<WorkspaceRevisionRequiredException>(() => service.CreateAsync(app, "0000", new() { DefinitionKey = "m3-quality" }));
        var tasks = ConcurrentDefinitions.Select(async key =>
        {
            try { await service.CreateAsync(app, "0000", new() { DefinitionKey = key, ExpectedRevision = 0 }); return true; }
            catch (WorkspaceRevisionConflictException) { return false; }
        });
        var results = await Task.WhenAll(tasks);
        Assert.Single(results, result => result);
        Assert.Single((await service.GetAsync(app, "0000"))!.Nodes);
        Assert.Equal(1, await workspace.RevisionAsync());
    }
}
