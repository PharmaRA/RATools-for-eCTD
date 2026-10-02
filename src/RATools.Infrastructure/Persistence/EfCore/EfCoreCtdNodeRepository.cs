using System.Data;
using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Ctd;
using RATools.Domain.Ctd;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class EfCoreCtdNodeRepository(RAToolsDbContext dbContext, NodeFileMoveGuard? moveGuard = null) : ICtdNodeRepository
{
    public async Task<CtdSequenceWorkspace?> GetSequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default)
    {
        if (dbContext.Database.CurrentTransaction is not null) return await ReadAsync(applicationId, sequenceNumber, cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var result = await ReadAsync(applicationId, sequenceNumber, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<CtdSequenceWorkspace?> ReadAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken)
    {
        var sequence = await dbContext.Sequences.AsNoTracking().SingleOrDefaultAsync(row =>
            row.ApplicationId == applicationId && row.SequenceNumber == sequenceNumber, cancellationToken);
        if (sequence is null) return null;
        var instances = await dbContext.CtdNodeInstances.AsNoTracking().Where(row => row.ApplicationId == applicationId).ToArrayAsync(cancellationToken);
        var graph = new CtdNodeGraph(applicationId, IchSectionDefinitions.Current, instances.Select(row => row.ToDomain()));
        var records = await dbContext.SequenceNodes.AsNoTracking().Where(row => row.ApplicationId == applicationId && row.SequenceNumber == sequenceNumber)
            .OrderBy(row => row.SortOrder).ThenBy(row => row.NodeInstanceId).ToArrayAsync(cancellationToken);
        var diagnostics = await dbContext.NodeBackfillDiagnostics.AsNoTracking()
            .Where(row => row.ApplicationId == applicationId && row.SequenceNumber == sequenceNumber)
            .OrderBy(row => row.PlacementId).ThenBy(row => row.Code)
            .Select(row => new CtdBackfillDiagnostic(row.PlacementId, row.Code, row.Message)).ToArrayAsync(cancellationToken);
        return new CtdSequenceWorkspace(graph, sequenceNumber, sequence.WorkspaceRevision,
            records.Select(row => row.ToDomain(graph)).ToArray(), diagnostics);
    }

    public async Task<long> SaveSequenceAsync(CtdNodeGraph graph, string sequenceNumber, IReadOnlyCollection<SequenceNode> nodes,
        long expectedRevision, CancellationToken cancellationToken = default)
    {
        await using var applicationLock = await new EfCoreWorkspaceRevisionStore(dbContext)
            .LockApplicationAsync(graph.ApplicationId, cancellationToken);
        if (moveGuard is not null) await moveGuard.EnsureReadyAsync(graph.ApplicationId, cancellationToken);
        return await new EfCorePersistenceTransaction(dbContext).ExecuteAsync(
            ct => SaveWithinTransactionAsync(graph, sequenceNumber, nodes, expectedRevision, ct), cancellationToken);
    }

    internal async Task<long> SaveWithinTransactionAsync(CtdNodeGraph graph, string sequenceNumber, IReadOnlyCollection<SequenceNode> nodes,
        long expectedRevision, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Node writes require an explicit database transaction.");
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(expectedRevision, 9007199254740991L);
        var nodeIds = nodes.Select(node => node.NodeInstanceId).ToHashSet();
        if (nodeIds.Count != nodes.Count) throw new CtdNodeConstraintException("DuplicateSequenceNode", "A sequence can contain a node instance only once.");
        foreach (var node in nodes)
        {
            if (node.ApplicationId != graph.ApplicationId || node.SequenceNumber != sequenceNumber || node.DefinitionVersion != graph.Definitions.Version)
                throw new CtdNodeConstraintException("SequenceNodeScopeMismatch", "All sequence nodes must share the supplied application, sequence and schema.", node.NodeInstanceId);
            if (graph.Get(node.NodeInstanceId).ParentInstanceId is { } parent && !nodeIds.Contains(parent))
                throw new CtdNodeConstraintException("SequenceNodeParentMissing", "The sequence must contain the parent of each node.", node.NodeInstanceId);
            _ = node.ToRecord(graph).ToDomain(graph);
        }

        var revised = await dbContext.Sequences.Where(row => row.ApplicationId == graph.ApplicationId && row.SequenceNumber == sequenceNumber &&
            row.WorkspaceRevision == expectedRevision).ExecuteUpdateAsync(updates => updates.SetProperty(row => row.WorkspaceRevision, expectedRevision + 1), cancellationToken);
        if (revised == 0)
        {
            var actual = await dbContext.Sequences.AsNoTracking().Where(row => row.ApplicationId == graph.ApplicationId && row.SequenceNumber == sequenceNumber)
                .Select(row => (long?)row.WorkspaceRevision).SingleOrDefaultAsync(cancellationToken);
            throw new WorkspaceRevisionConflictException(expectedRevision, actual);
        }
        foreach (var entry in dbContext.ChangeTracker.Entries<SequenceRecord>().Where(entry =>
                     entry.Entity.ApplicationId == graph.ApplicationId && entry.Entity.SequenceNumber == sequenceNumber).ToArray())
        {
            var revision = entry.Property(row => row.WorkspaceRevision);
            revision.CurrentValue = expectedRevision + 1;
            revision.OriginalValue = expectedRevision + 1;
            revision.IsModified = false;
        }

        var existingInstances = await dbContext.CtdNodeInstances.AsNoTracking().Where(row => row.ApplicationId == graph.ApplicationId)
            .ToDictionaryAsync(row => row.Id, cancellationToken);
        foreach (var node in graph.Nodes.Values)
        {
            if (existingInstances.TryGetValue(node.Id, out var existing))
            {
                var stored = existing.ToDomain();
                if (stored.IdentityKey != node.IdentityKey || stored.IdentityStatus != node.IdentityStatus || stored.DefinitionVersion != node.DefinitionVersion)
                    throw new CtdNodeConstraintException("NodeIdentityChangeRequiresNewInstance", "Existing application node identities are immutable.", node.Id);
            }
            else dbContext.CtdNodeInstances.Add(node.ToRecord(graph));
        }
        var existingNodes = await dbContext.SequenceNodes.Where(row => row.ApplicationId == graph.ApplicationId && row.SequenceNumber == sequenceNumber)
            .ToDictionaryAsync(row => row.NodeInstanceId, cancellationToken);
        foreach (var node in nodes)
        {
            var record = node.ToRecord(graph);
            if (existingNodes.TryGetValue(node.NodeInstanceId, out var existing))
                dbContext.Entry(existing).CurrentValues.SetValues(record);
            else dbContext.SequenceNodes.Add(record);
        }
        dbContext.SequenceNodes.RemoveRange(existingNodes.Values.Where(row => !nodeIds.Contains(row.NodeInstanceId)));
        await dbContext.SaveChangesAsync(cancellationToken);
        return expectedRevision + 1;
    }
}
