using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.Requests;

namespace RATools.Application.Applications;

public interface ISequencePublishingMetadataService
{
    Task<SequencePublishingMetadataDto?> GetAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default);

    Task<SequencePublishingMetadataDto?> UpdateAsync(
        Guid applicationId,
        string sequenceNumber,
        UpdateSequencePublishingMetadataRequest request,
        CancellationToken cancellationToken = default);
}
