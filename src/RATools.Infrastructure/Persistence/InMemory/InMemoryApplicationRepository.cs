using RATools.Infrastructure.Persistence.EfCore;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Applications;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryApplicationRepository : IApplicationRepository
{
    private readonly TransactionalMemoryCollection<SubmissionApplication> _items = new(item => item.ToRecord().ToDomain());

    public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default)
    {
        _items.Set(application.Id, application);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default)
    {
        _items.Set(application.Id, application);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _items.Remove(id);
        return Task.CompletedTask;
    }

    public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var application = _items.Get(id);
        return Task.FromResult(application);
    }

    public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<SubmissionApplication> applications = _items.Values
            .OrderBy(x => x.CreatedUtc)
            .ToArray();

        return Task.FromResult(applications);
    }
}
