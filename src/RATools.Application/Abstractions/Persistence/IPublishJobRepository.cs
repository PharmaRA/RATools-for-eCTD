using RATools.Domain.Publishing;

namespace RATools.Application.Abstractions.Persistence;

public interface IPublishJobRepository
{
    Task AddAsync(PublishJob job, CancellationToken cancellationToken = default);

    Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default);

    Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default);

    Task<PublishJobHistoryQueryResult> QueryHistoryAsync(PublishJobHistoryQuery query, CancellationToken cancellationToken = default);
}
