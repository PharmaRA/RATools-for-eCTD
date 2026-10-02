using System.Collections.Concurrent;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Ctd;
using RATools.Application.Workspaces;
using RATools.Domain.Ctd;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryCtdNodeRepository(IWorkspaceRevisionStore revisions,
    IDocumentPlacementRepository placements, NodeFileMoveGuard? moveGuard = null) : ICtdNodeRepository
{
    private readonly ConcurrentDictionary<(Guid, string), CtdSequenceWorkspace> _workspaces = new();

    internal async Task StageImportAsync(CtdNodeGraph graph, string sequenceNumber, IReadOnlyList<SequenceNode> nodes,
        long expectedRevision, IReadOnlyList<CtdBackfillDiagnostic> diagnostics, CancellationToken cancellationToken)
    {
        var transaction = InMemoryPersistenceTransaction.Current.Value
            ?? throw new InvalidOperationException("Imported nodes require a transaction.");
        if (_workspaces.ContainsKey((graph.ApplicationId, sequenceNumber)))
            throw new InvalidOperationException("An import cannot overwrite existing nodes.");
        if (!await revisions.AdvanceAsync(graph.ApplicationId, sequenceNumber, expectedRevision, cancellationToken))
            throw new WorkspaceRevisionConflictException(expectedRevision, null);
        var saved = new CtdSequenceWorkspace(graph, sequenceNumber, expectedRevision + 1, nodes.ToArray(), diagnostics);
        transaction.Commits.Add(() => _workspaces[(graph.ApplicationId, sequenceNumber)] = saved);
    }

    public async Task<CtdSequenceWorkspace?> GetSequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
    {
        var revision = await revisions.GetRevisionAsync(applicationId, sequenceNumber, cancellationToken);
        if (revision is null) return null;
        var instances = _workspaces.Where(pair => pair.Key.Item1 == applicationId).SelectMany(pair => pair.Value.Graph.Nodes.Values)
            .DistinctBy(node => node.Id).ToArray();
        var graph = new CtdNodeGraph(applicationId, IchSectionDefinitions.Current, instances);
        return _workspaces.TryGetValue((applicationId, sequenceNumber), out var existing)
            ? existing with { Graph = graph, WorkspaceRevision = revision.Value }
            : new CtdSequenceWorkspace(graph, sequenceNumber, revision.Value, [], []);
    }

    public async Task<long> SaveSequenceAsync(CtdNodeGraph graph, string sequenceNumber, IReadOnlyCollection<SequenceNode> nodes,
        long expectedRevision, CancellationToken cancellationToken = default)
    {
        // The singleton repository shares the same application lock as document edits.
        var coordinator = new WorkspaceMutationCoordinator(revisions, new InMemoryPersistenceTransaction(), moveGuard);
        await using var mutation = await coordinator.AcquireAsync(graph.ApplicationId, sequenceNumber, expectedRevision, cancellationToken);
        var old = await GetSequenceAsync(graph.ApplicationId, sequenceNumber, cancellationToken)
            ?? throw new WorkspaceRevisionTargetNotFoundException();
        var ids = nodes.Select(node => node.NodeInstanceId).ToHashSet();
        if (ids.Count != nodes.Count) throw new InvalidOperationException("Duplicate sequence node.");
        foreach (var instance in old.Graph.Nodes.Values)
            if (!graph.Nodes.TryGetValue(instance.Id, out var candidate) || candidate.IdentityKey != instance.IdentityKey ||
                candidate.IdentityStatus != instance.IdentityStatus || candidate.DefinitionVersion != instance.DefinitionVersion)
                throw new InvalidOperationException("Application node identities cannot be removed or changed.");
        foreach (var node in nodes)
        {
            if (node.ApplicationId != graph.ApplicationId || node.SequenceNumber != sequenceNumber || node.DefinitionVersion != graph.Definitions.Version ||
                (graph.Get(node.NodeInstanceId).ParentInstanceId is { } parent && !ids.Contains(parent)))
                throw new InvalidOperationException("Invalid sequence node scope or missing parent.");
            _ = new SequenceNode(graph, sequenceNumber, node.NodeInstanceId, node.Attributes, node.Title, node.SortOrder, node.StorageSegment);
        }
        if ((await placements.ListAsync(cancellationToken)).Any(item => item.ApplicationId == graph.ApplicationId &&
            item.SequenceNumber == sequenceNumber && item.NodeInstanceId is { } id && !ids.Contains(id)))
            throw new InvalidOperationException("A node referenced by a placement cannot be removed.");
        var saved = new CtdSequenceWorkspace(new CtdNodeGraph(graph.ApplicationId, graph.Definitions, graph.Nodes.Values),
            sequenceNumber, expectedRevision + 1, nodes.ToArray(), old.Diagnostics);
        await mutation.CommitAsync(_ =>
        {
            InMemoryPersistenceTransaction.Current.Value!.Commits.Add(() => _workspaces[(graph.ApplicationId, sequenceNumber)] = saved);
            return Task.CompletedTask;
        }, cancellationToken);
        return mutation.Revision;
    }
}
