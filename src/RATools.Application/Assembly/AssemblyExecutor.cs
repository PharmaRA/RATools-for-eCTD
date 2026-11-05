using RATools.Application.Abstractions.Persistence;
using RATools.Application.Finalization;
using RATools.Domain.Documents;

namespace RATools.Application.Assembly;

// Kept as a source compatibility boundary. Metadata-only execution cannot prove
// immutable inputs or durable outcomes; every caller must use AssemblyService.
public sealed class AssemblyExecutor
{
    public AssemblyExecutor(AssemblyPlanner planner, IDocumentPlacementRepository placementRepository,
        ICtdNodeRepository nodeRepository, IWorkspaceRevisionStore revisions,
        IPersistenceTransaction transactions, FinalizedWorkspaceGuard? finalization = null)
    {
        _ = planner; _ = placementRepository; _ = nodeRepository; _ = revisions; _ = transactions; _ = finalization;
    }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static",
        Justification = "The retired instance API is retained solely to fail closed for existing source callers.")]
    public Task<AssemblyExecutionResult> ExecuteAsync(Guid applicationId, string sequenceNumber,
        long expectedRevision, string planDigest, string csv, CancellationToken cancellationToken = default) =>
        Task.FromException<AssemblyExecutionResult>(new AssemblyPlanInvalidException(
            "Metadata-only assembly execution is retired. Use a stored AssemblyService preview, explicit selection and durable execution job."));
}

public sealed record AssemblyExecutionResult(Guid ApplicationId, string SequenceNumber, long WorkspaceRevision,
    string PlanDigest, IReadOnlyList<Guid> PlacementIds);

public sealed class AssemblyPlanStaleException(string message) : InvalidOperationException(message);
public sealed class AssemblyPlanInvalidException(string message) : InvalidOperationException(message);
public sealed class AssemblyApplicationNotFoundException(string message) : KeyNotFoundException(message);
