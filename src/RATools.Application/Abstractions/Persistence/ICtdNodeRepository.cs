using RATools.Domain.Ctd;

namespace RATools.Application.Abstractions.Persistence;

public sealed record CtdSequenceWorkspace(CtdNodeGraph Graph, string SequenceNumber, long WorkspaceRevision,
    IReadOnlyList<SequenceNode> Nodes, IReadOnlyList<CtdBackfillDiagnostic> Diagnostics);

public sealed record CtdBackfillDiagnostic(Guid PlacementId, string Code, string Message);

public sealed class WorkspaceRevisionConflictException(long expectedRevision, long? currentRevision)
    : InvalidOperationException("The workspace revision changed. Reload it before writing.")
{
    public long ExpectedRevision { get; } = expectedRevision;
    public long? CurrentRevision { get; } = currentRevision;
}

public interface ICtdNodeRepository
{
    Task<CtdSequenceWorkspace?> GetSequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default);

    Task<long> SaveSequenceAsync(CtdNodeGraph graph, string sequenceNumber, IReadOnlyCollection<SequenceNode> nodes,
        long expectedRevision, CancellationToken cancellationToken = default);
}
