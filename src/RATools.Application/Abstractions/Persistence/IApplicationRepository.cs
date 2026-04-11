using RATools.Domain.Applications;

namespace RATools.Application.Abstractions.Persistence;

public interface IApplicationRepository
{
    Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default);

    Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default);
}
