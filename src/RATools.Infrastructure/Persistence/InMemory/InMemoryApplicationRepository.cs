using System.Collections.Concurrent;
using RATools.Application.Abstractions.Persistence;
using RATools.Domain.Applications;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryApplicationRepository : IApplicationRepository
{
    private readonly ConcurrentDictionary<Guid, SubmissionApplication> _items = new();

    public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default)
    {
        _items[application.Id] = application;
        return Task.CompletedTask;
    }

    public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default)
    {
        _items[application.Id] = application;
        return Task.CompletedTask;
    }

    public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        _items.TryGetValue(id, out var application);
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
