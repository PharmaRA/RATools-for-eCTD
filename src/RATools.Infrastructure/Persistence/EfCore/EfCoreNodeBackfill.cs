using Microsoft.EntityFrameworkCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Ctd;

namespace RATools.Infrastructure.Persistence.EfCore;

public sealed record NodeBackfillResult(Guid ApplicationId, string SequenceNumber, bool Preview, bool Changed,
    long WorkspaceRevision, int BoundPlacements, IReadOnlyList<CtdBackfillDiagnostic> Diagnostics);

public sealed class EfCoreNodeBackfill(RAToolsDbContext dbContext)
{
    public async Task<NodeBackfillResult> RunAsync(Guid applicationId, string sequenceNumber, bool preview,
        CancellationToken cancellationToken = default)
    {
        await using var applicationLock = await new EfCoreWorkspaceRevisionStore(dbContext)
            .LockApplicationAsync(applicationId, cancellationToken);
        return await new EfCorePersistenceTransaction(dbContext).ExecuteAsync(async ct =>
    {
        var repository = new EfCoreCtdNodeRepository(dbContext);
        var workspace = await repository.GetSequenceAsync(applicationId, sequenceNumber, ct)
            ?? throw new InvalidOperationException("The backfill sequence does not exist.");
        var placements = await dbContext.DocumentPlacements.Where(row => row.ApplicationId == applicationId && row.SequenceNumber == sequenceNumber)
            .OrderBy(row => row.Id).ToArrayAsync(ct);
        var inputs = placements.Select(row => new LegacyPlacementNodeInput(row.Id, row.CtdSection, row.NodeInstanceId)).ToArray();
        var plan = LegacyNodeBackfillPlan.Create(workspace, inputs);
        var digest = LegacyNodeBackfillPlan.InputDigest(inputs);
        var checkpoint = await dbContext.NodeBackfillCheckpoints.SingleOrDefaultAsync(row => row.ApplicationId == applicationId &&
            row.SequenceNumber == sequenceNumber && row.Version == LegacyNodeBackfillPlan.Version, ct);
        var diagnosticsMatch = workspace.Diagnostics.OrderBy(item => item.PlacementId).ThenBy(item => item.Code)
            .SequenceEqual(plan.Diagnostics.OrderBy(item => item.PlacementId).ThenBy(item => item.Code));
        var changed = plan.Bindings.Count > 0 || !diagnosticsMatch;
        if (preview || (!changed && checkpoint?.InputDigest == digest))
            return new NodeBackfillResult(applicationId, sequenceNumber, preview, changed, workspace.WorkspaceRevision, plan.Bindings.Count, plan.Diagnostics);

        foreach (var placement in placements)
        {
            if (plan.Bindings.TryGetValue(placement.Id, out var nodeId))
            {
                placement.NodeInstanceId = nodeId;
                placement.CtdSection = plan.Graph.GetSectionPath(nodeId);
            }
        }
        var storedDiagnostics = await dbContext.NodeBackfillDiagnostics.Where(row => row.ApplicationId == applicationId && row.SequenceNumber == sequenceNumber)
            .ToArrayAsync(ct);
        var planned = plan.Diagnostics.ToDictionary(item => (item.PlacementId, item.Code));
        dbContext.NodeBackfillDiagnostics.RemoveRange(storedDiagnostics.Where(row => !planned.ContainsKey((row.PlacementId, row.Code))));
        foreach (var diagnostic in plan.Diagnostics)
        {
            var stored = storedDiagnostics.SingleOrDefault(row => row.PlacementId == diagnostic.PlacementId && row.Code == diagnostic.Code);
            if (stored is null) dbContext.NodeBackfillDiagnostics.Add(new NodeBackfillDiagnosticRecord
            {
                ApplicationId = applicationId, SequenceNumber = sequenceNumber, PlacementId = diagnostic.PlacementId,
                Code = diagnostic.Code, Message = diagnostic.Message
            });
            else stored.Message = diagnostic.Message;
        }
        if (checkpoint is null)
        {
            checkpoint = new NodeBackfillCheckpointRecord { ApplicationId = applicationId, SequenceNumber = sequenceNumber, Version = LegacyNodeBackfillPlan.Version };
            dbContext.NodeBackfillCheckpoints.Add(checkpoint);
        }
        checkpoint.InputDigest = LegacyNodeBackfillPlan.InputDigest(placements.Select(row => new LegacyPlacementNodeInput(row.Id, row.CtdSection, row.NodeInstanceId)));
        checkpoint.PlacementCount = placements.Length;
        checkpoint.UnresolvedCount = plan.Diagnostics.Select(item => item.PlacementId).Distinct().Count();
        checkpoint.CompletedUtc = DateTime.UtcNow;
        var revision = workspace.WorkspaceRevision;
        if (changed) revision = await repository.SaveWithinTransactionAsync(plan.Graph, sequenceNumber, plan.Nodes, revision, ct);
        else await dbContext.SaveChangesAsync(ct);
        return new NodeBackfillResult(applicationId, sequenceNumber, false, changed, revision, plan.Bindings.Count, plan.Diagnostics);
        }, cancellationToken);
    }
}
