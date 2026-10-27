using RATools.Application.Abstractions.Persistence;
using RATools.Application.Finalization;
using RATools.Domain.Documents;

namespace RATools.Application.Assembly;

// Executes a previously previewed plan. The plan is regenerated from the same inputs and
// its digest compared; when the manifest, workspace revision or resolved nodes changed,
// the execution is refused instead of applying a stale preview. Every selected row
// commits in one transaction together with the revision advance, or nothing commits.
public sealed class AssemblyExecutor(
    AssemblyPlanner planner,
    IDocumentPlacementRepository placementRepository,
    ICtdNodeRepository nodeRepository,
    IWorkspaceRevisionStore revisions,
    IPersistenceTransaction transactions,
    FinalizedWorkspaceGuard? finalization = null)
{
    public async Task<AssemblyExecutionResult> ExecuteAsync(Guid applicationId, string sequenceNumber,
        long expectedRevision, string planDigest, string csv, CancellationToken cancellationToken = default)
    {
        var plan = await planner.PlanAsync(applicationId, sequenceNumber, expectedRevision, csv, cancellationToken)
            .ConfigureAwait(false);
        if (!string.Equals(plan.PlanDigest, planDigest, StringComparison.Ordinal))
            throw new AssemblyPlanStaleException("The plan digest does not match the current preview; regenerate it before executing.");
        if (plan.InvalidRows > 0)
            throw new AssemblyPlanInvalidException($"The plan contains {plan.InvalidRows} invalid row(s); fix or remove them before executing.");
        var selected = plan.Rows.Where(row => row.Status == AssemblyRowStatus.Valid).ToArray();
        if (selected.Length == 0) throw new AssemblyPlanInvalidException("The plan has no executable rows.");

        var workspace = await nodeRepository.GetSequenceAsync(applicationId, sequenceNumber, cancellationToken).ConfigureAwait(false)
            ?? throw new AssemblyApplicationNotFoundException($"Sequence {sequenceNumber} does not exist on application {applicationId}.");
        var nodes = workspace.Nodes.ToDictionary(node => node.NodeInstanceId);
        var lockScope = await revisions.LockApplicationAsync(applicationId, cancellationToken).ConfigureAwait(false);
        try
        {
            // Bulk assembly is an ordinary draft write: finalized content must stay frozen
            // until an explicit continuation exists.
            if (finalization is not null)
                await finalization.EnsureEditableAsync(applicationId, sequenceNumber, cancellationToken).ConfigureAwait(false);
            return await transactions.ExecuteAsync(async ct =>
            {
                var current = await revisions.GetRevisionAsync(applicationId, sequenceNumber, ct).ConfigureAwait(false);
                if (current != expectedRevision)
                    throw new AssemblyPlanStaleException(
                        $"The workspace revision changed from {expectedRevision} to {current}. Regenerate the preview.");
                var existing = await placementRepository.ListBySequenceAsync(applicationId, sequenceNumber, ct).ConfigureAwait(false);
                var usedLeafIds = existing.Select(placement => placement.LeafId).ToHashSet(StringComparer.Ordinal);
                var created = new List<DocumentPlacement>(selected.Length);
                foreach (var row in selected)
                {
                    var node = nodes[Guid.Parse(row.NodeInstanceId!)];
                    var placement = new DocumentPlacement(Guid.Parse(row.DocumentId!), applicationId, sequenceNumber,
                        node.CtdSection, Enum.Parse<DocumentPlacementOperation>(row.Operation, ignoreCase: true), row.Title,
                        NewLeafId(usedLeafIds));
                    placement.BindToNode(node, row.SortOrder);
                    if (row.TargetLeafAddress is { } target)
                        placement.ReviseLifecycleTarget(Guid.Parse(target));
                    await placementRepository.AddAsync(placement, ct).ConfigureAwait(false);
                    usedLeafIds.Add(placement.LeafId);
                    created.Add(placement);
                }
                if (!await revisions.AdvanceAsync(applicationId, sequenceNumber, expectedRevision, ct).ConfigureAwait(false))
                    throw new AssemblyPlanStaleException("The workspace revision changed during execution.");
                return new AssemblyExecutionResult(applicationId, sequenceNumber, expectedRevision + 1,
                    plan.PlanDigest, created.Select(placement => placement.Id).ToArray());
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await lockScope.DisposeAsync().ConfigureAwait(false);
        }
    }

    // A fresh leaf identity that cannot collide with an existing placement in the sequence.
    private static string NewLeafId(HashSet<string> used)
    {
        while (true)
        {
            var candidate = "leaf-" + Guid.NewGuid().ToString("N");
            if (used.Add(candidate)) return candidate;
        }
    }
}

public sealed record AssemblyExecutionResult(Guid ApplicationId, string SequenceNumber, long WorkspaceRevision,
    string PlanDigest, IReadOnlyList<Guid> PlacementIds);

public sealed class AssemblyPlanStaleException(string message) : InvalidOperationException(message);
public sealed class AssemblyPlanInvalidException(string message) : InvalidOperationException(message);
public sealed class AssemblyApplicationNotFoundException(string message) : KeyNotFoundException(message);
