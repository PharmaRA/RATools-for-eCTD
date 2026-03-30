using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Requests;

namespace RATools.Application.Publishing;

public interface IPublishJobService
{
    Task<PublishJobDto> CreateAsync(CreatePublishJobRequest request, CancellationToken cancellationToken = default);

    Task<PublishJobDto?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PublishJobDto>> ListAsync(CancellationToken cancellationToken = default);
}
