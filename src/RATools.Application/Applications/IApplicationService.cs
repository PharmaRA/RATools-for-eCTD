using RATools.Application.Applications.Dtos;
using RATools.Application.Applications.Requests;

namespace RATools.Application.Applications;

public interface IApplicationService
{
    Task<ApplicationDto> CreateAsync(CreateApplicationRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<ApplicationDto>> ListAsync(CancellationToken cancellationToken = default);

    Task<ApplicationDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<ApplicationDto?> CreateSequenceAsync(Guid applicationId, CreateSequenceRequest request, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid applicationId, CancellationToken cancellationToken = default);

    Task<bool> DeleteSequenceAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default);
}
