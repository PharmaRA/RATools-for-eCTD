using RATools.Domain.RegionalMetadata;

namespace RATools.Application.RegionalMetadata;

public interface IRegionalMetadataStore
{
    Task<RegionalMetadataRevision?> GetHeadAsync(Guid applicationId, string? sequenceNumber, CancellationToken cancellationToken = default);
    Task<RegionalMetadataRevision?> GetRevisionAsync(Guid id, CancellationToken cancellationToken = default);
    Task<RegionalMetadataRevision?> FindLegacyAsync(Guid applicationId, string sequenceNumber, string legacyDigest, CancellationToken cancellationToken = default);
    Task<RegionalMetadataRevision> AppendAsync(RegionalMetadataRevision revision, Guid? expectedHeadId, CancellationToken cancellationToken = default);
}
public sealed class RegionalMetadataConflictException() : InvalidOperationException("The regional metadata head changed; refresh and retry.");
public sealed class RegionalMetadataScopeException(string message) : InvalidOperationException(message);

// All archived revisions participate, including ones superseded by a newer head.
public interface IRegionalEvidenceReferences
{
    Task<bool> HasPlacementAsync(Guid placementId, CancellationToken cancellationToken = default);
    Task<bool> HasNodesAsync(Guid applicationId, string sequenceNumber, IReadOnlyCollection<Guid> nodeIds,
        CancellationToken cancellationToken = default);
}

public sealed class RegionalEvidenceReferencedException() : InvalidOperationException(
    "This draft object is referenced by archived regional metadata and must be retained. Removing context from the current revision does not remove historical evidence.");
