using RATools.Application.Abstractions.Persistence;
using RATools.Application.Workspaces;
using RATools.Domain.Ctd;

namespace RATools.Application.Ctd;

public sealed class CtdNodeService(ICtdNodeRepository repository, IApplicationRepository applications,
    IDocumentPlacementRepository placements, WorkspaceMutationCoordinator mutations,
    IWorkspaceRevisionStore revisions, CtdNodePathResolver paths)
{
    public static CtdNodeTreeDto Project(CtdSequenceWorkspace workspace) => new(workspace.Graph.ApplicationId,
        workspace.SequenceNumber, workspace.WorkspaceRevision, workspace.Graph.Definitions.Version,
        workspace.Graph.Definitions.Definitions.Values.ToArray(), workspace.Nodes.Select(node =>
        {
            var instance = workspace.Graph.Get(node.NodeInstanceId);
            return new CtdNodeDto(node.NodeInstanceId, instance.ParentInstanceId, instance.DefinitionKey, node.CtdSection,
                node.Attributes, node.Title, node.SortOrder, node.StorageSegment, node.MetadataStatus.ToString(),
                instance.IdentityStatus.ToString(), node.AllowsLeaves,
                workspace.Graph.Definitions.Get(instance.DefinitionKey).ValidateAttributes(node.Attributes));
        }).ToArray(), workspace.Diagnostics);

    public async Task<CtdNodeTreeDto?> GetAsync(Guid applicationId, string sequenceNumber, CancellationToken ct = default)
    {
        await using var held = await revisions.LockApplicationAsync(applicationId, ct);
        var workspace = await repository.GetSequenceAsync(applicationId, sequenceNumber, ct);
        return workspace is null ? null : Project(workspace);
    }

    public Task<CtdNodeTreeDto> CreateAsync(Guid appId, string sequence, CreateCtdNodeRequest request, CancellationToken ct = default) =>
        EditAsync(appId, sequence, request.ExpectedRevision, async workspace =>
        {
            var definition = Definition(workspace, request.DefinitionKey);
            if (request.ParentInstanceId is { } parent && workspace.Nodes.All(node => node.NodeInstanceId != parent))
                throw Error("SequenceNodeParentMissing", "Create or include the parent in this sequence first.", parent);
            var candidate = CtdNodeInstance.Rehydrate(Guid.NewGuid(), appId, request.ParentInstanceId, workspace.Graph.Definitions, definition.DefinitionKey, request.Attributes);
            var matching = workspace.Graph.Nodes.Values.Where(node => node.IdentityKey == candidate.IdentityKey && node.IdentityStatus == CtdIdentityStatus.Resolved).ToArray();
            var instance = matching.Length == 1 ? matching[0] : candidate;
            if (workspace.Nodes.Any(node => node.NodeInstanceId == instance.Id))
                throw Error("NodeIdentityConflict", "This business instance is already present in the sequence.", instance.Id);
            if (instance == candidate) workspace.Graph.Add(instance);
            var node = new SequenceNode(workspace.Graph, sequence, instance.Id, request.Attributes, request.Title,
                request.SortOrder, request.StorageSegment);
            var result = workspace with { Nodes = workspace.Nodes.Append(node).ToArray() };
            await ValidatePathsAsync(result, [node], ct);
            return result;
        }, ct);

    public Task<CtdNodeTreeDto> UpdateAsync(Guid appId, string sequence, Guid nodeId, UpdateCtdNodeRequest request, CancellationToken ct = default) =>
        EditAsync(appId, sequence, request.ExpectedRevision, async workspace =>
        {
            var old = Node(workspace, nodeId);
            if (old.StorageSegment is not null && request.StorageSegment != old.StorageSegment)
                throw Error("NodeStorageSegmentImmutable", "An allocated directory segment cannot be changed by a metadata edit.", nodeId);
            var revised = new SequenceNode(workspace.Graph, sequence, nodeId, request.Attributes, request.Title, request.SortOrder, request.StorageSegment);
            var result = workspace with { Nodes = workspace.Nodes.Select(node => node.NodeInstanceId == nodeId ? revised : node).ToArray() };
            // Imported incomplete nodes remain viewable. Saving a title must not invent identity attributes.
            if (revised.MetadataStatus == NodeMetadataStatus.Complete && old.StorageSegment != revised.StorageSegment)
                await ValidatePathsAsync(result, [revised], ct);
            return result;
        }, ct);

    public Task<CtdNodeTreeDto> DeleteAsync(Guid appId, string sequence, Guid nodeId, long? revision, CancellationToken ct = default) =>
        EditAsync(appId, sequence, revision, async workspace =>
        {
            _ = Node(workspace, nodeId);
            if (workspace.Nodes.Any(node => workspace.Graph.Get(node.NodeInstanceId).ParentInstanceId == nodeId) ||
                (await placements.ListBySequenceAsync(appId, sequence, ct)).Any(leaf => leaf.NodeInstanceId == nodeId))
                throw Error("NodeNotEmpty", "Only an empty sequence node can be removed. Remove its children or placements first.", nodeId);
            return workspace with { Nodes = workspace.Nodes.Where(node => node.NodeInstanceId != nodeId).ToArray() };
        }, ct);

    public Task<CtdNodeTreeDto> InheritAsync(Guid appId, string sequence, InheritCtdNodesRequest request, CancellationToken ct = default) =>
        EditAsync(appId, sequence, request.ExpectedRevision, async workspace =>
        {
            if (string.CompareOrdinal(request.SourceSequenceNumber, sequence) >= 0)
                throw Error("HistoricalSequenceRequired", "Select an earlier sequence.");
            var source = await repository.GetSequenceAsync(appId, request.SourceSequenceNumber, ct)
                ?? throw new WorkspaceRevisionTargetNotFoundException();
            var existing = workspace.Nodes.Select(node => node.NodeInstanceId).ToHashSet();
            var included = source.Nodes.Where(node => !existing.Contains(node.NodeInstanceId)).Select(node =>
                new SequenceNode(workspace.Graph, sequence, node.NodeInstanceId, node.Attributes, node.Title, node.SortOrder, node.StorageSegment));
            return workspace with { Nodes = workspace.Nodes.Concat(included).ToArray() };
        }, ct);

    public Task<CtdNodeTreeDto> CloneAsync(Guid appId, string sequence, Guid nodeId, UpdateCtdNodeRequest request, CancellationToken ct = default) =>
        EditAsync(appId, sequence, request.ExpectedRevision, async workspace => (await PlanCloneAsync(workspace, nodeId, request, ct)).Workspace, ct);

    public async Task<CtdNodeClonePreview> PreviewCloneAsync(Guid appId, string sequence, Guid nodeId, UpdateCtdNodeRequest request, CancellationToken ct = default)
    {
        await using var held = await mutations.AcquireAsync(appId, sequence, request.ExpectedRevision, ct);
        var workspace = await repository.GetSequenceAsync(appId, sequence, ct) ?? throw new WorkspaceRevisionTargetNotFoundException();
        var plan = await PlanCloneAsync(workspace, nodeId, request, ct);
        var ids = plan.Added.Select(node => node.NodeInstanceId).ToHashSet();
        return new(workspace.WorkspaceRevision, Project(plan.Workspace).Nodes.Where(node => ids.Contains(node.NodeInstanceId)).ToArray(),
            await ValidatePathsAsync(plan.Workspace, plan.Added, ct));
    }

    private async Task<(CtdSequenceWorkspace Workspace, SequenceNode[] Added)> PlanCloneAsync(CtdSequenceWorkspace workspace,
        Guid nodeId, UpdateCtdNodeRequest request, CancellationToken ct)
    {
        var source = Node(workspace, nodeId);
        var instance = workspace.Graph.Get(nodeId);
        if (!workspace.Graph.Definitions.Get(instance.DefinitionKey).Repeatable)
            throw Error("NodeNotRepeatable", "Only repeatable business groups can be copied.", nodeId);
        var added = new List<SequenceNode>();
        void Copy(SequenceNode old, Guid? parent, bool root)
        {
            var attributes = (root ? request.Attributes : old.Attributes).ToDictionary();
            if (attributes.ContainsKey("ID")) attributes["ID"] = "node-" + Guid.NewGuid().ToString("N");
            var node = workspace.Graph.Create(workspace.Graph.Get(old.NodeInstanceId).DefinitionKey, parent, attributes);
            added.Add(new SequenceNode(workspace.Graph, workspace.SequenceNumber, node.Id, attributes,
                root ? request.Title : old.Title, root ? request.SortOrder : old.SortOrder, root ? request.StorageSegment : old.StorageSegment));
            foreach (var child in workspace.Nodes.Where(child => workspace.Graph.Get(child.NodeInstanceId).ParentInstanceId == old.NodeInstanceId))
                Copy(child, node.Id, false);
        }
        Copy(source, instance.ParentInstanceId, true);
        var result = workspace with { Nodes = workspace.Nodes.Concat(added).ToArray() };
        await ValidatePathsAsync(result, added, ct);
        return (result, added.ToArray());
    }

    private async Task<CtdNodeTreeDto> EditAsync(Guid appId, string sequence, long? revision,
        Func<CtdSequenceWorkspace, Task<CtdSequenceWorkspace>> edit, CancellationToken ct)
    {
        CtdSequenceWorkspace result;
        // SaveSequenceAsync owns its transaction and lock. Prepare under the same application
        // lock, release it, then compare-and-swap the revision in that repository transaction.
        // Any intervening edit fails the save, including newly attached leaves before deletion.
        await using (var held = await mutations.AcquireAsync(appId, sequence, revision, ct))
        {
            var workspace = await repository.GetSequenceAsync(appId, sequence, ct) ?? throw new WorkspaceRevisionTargetNotFoundException();
            try { result = await edit(workspace); }
            catch (Exception error) when (error is ArgumentException or System.Xml.XmlException)
            {
                throw Error("InvalidNodeMetadata", error.Message);
            }
            await ValidateXmlIdsAsync(result, ct);
        }
        var savedRevision = await repository.SaveSequenceAsync(result.Graph, sequence, result.Nodes, revision!.Value, ct);
        return Project(result with { WorkspaceRevision = savedRevision });
    }

    private async Task<IReadOnlyDictionary<Guid, string>> ValidatePathsAsync(CtdSequenceWorkspace workspace, IEnumerable<SequenceNode> added, CancellationToken ct)
    {
        var app = await applications.GetAsync(workspace.Graph.ApplicationId, ct) ?? throw new WorkspaceRevisionTargetNotFoundException();
        var result = added.ToDictionary(node => node.NodeInstanceId, node => paths.Resolve(app.EctdTemplateKey, workspace, node.NodeInstanceId));
        foreach (var node in workspace.Nodes.Where(node => !result.ContainsKey(node.NodeInstanceId) && node.MetadataStatus == NodeMetadataStatus.Complete))
        {
            string existing;
            try { existing = paths.Resolve(app.EctdTemplateKey, workspace, node.NodeInstanceId); }
            catch (CtdNodeConstraintException error) when (error.Code is "NodeMetadataIncomplete" or "NodeStorageSegmentRequired") { continue; }
            if (result.Values.Contains(existing, StringComparer.OrdinalIgnoreCase))
                throw Error("NodeDirectoryConflict", "Choose a distinct directory segment for the new business group.", node.NodeInstanceId);
        }
        if (result.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count() != result.Count)
            throw Error("NodeDirectoryConflict", "The copied structure contains overlapping directories.");
        return result;
    }

    private async Task ValidateXmlIdsAsync(CtdSequenceWorkspace workspace, CancellationToken ct)
    {
        var ids = (await placements.ListBySequenceAsync(workspace.Graph.ApplicationId, workspace.SequenceNumber, ct))
            .Select(leaf => leaf.LeafId).ToHashSet(StringComparer.Ordinal);
        foreach (var node in workspace.Nodes)
            if (node.Attributes.TryGetValue("ID", out var id) && !ids.Add(id))
                throw Error("DuplicateXmlId", "Node XML IDs must be unique within the sequence.", node.NodeInstanceId);
    }

    private static SequenceNode Node(CtdSequenceWorkspace workspace, Guid id) => workspace.Nodes.SingleOrDefault(node => node.NodeInstanceId == id)
        ?? throw Error("NodeNotFound", "The node is not in this sequence.", id);

    private static SectionDefinition Definition(CtdSequenceWorkspace workspace, string key) => workspace.Graph.Definitions.Definitions.TryGetValue(key, out var definition)
        ? definition : throw Error("UnsupportedNodeSchema", "Select a definition in the pinned ICH schema.");

    private static CtdNodeConstraintException Error(string code, string message, Guid? id = null) => new(code, message, id);
}
