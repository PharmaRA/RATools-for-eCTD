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
