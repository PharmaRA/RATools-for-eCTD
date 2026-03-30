using RATools.Application.Abstractions.Persistence;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Publishing.Requests;
using RATools.Domain.Publishing;

namespace RATools.Application.Publishing;

public sealed class PublishJobService(IPublishJobRepository repository) : IPublishJobService
{
    public async Task<PublishJobDto> CreateAsync(CreatePublishJobRequest request, CancellationToken cancellationToken = default)
    {
        var job = new PublishJob(request.ApplicationId, request.SequenceNumber);
        await repository.AddAsync(job, cancellationToken);
        return job.ToDto();
    }

    public async Task<PublishJobDto?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var job = await repository.GetAsync(id, cancellationToken);
        return job?.ToDto();
    }

    public async Task<IReadOnlyCollection<PublishJobDto>> ListAsync(CancellationToken cancellationToken = default)
    {
        var jobs = await repository.ListAsync(cancellationToken);
        return jobs.Select(x => x.ToDto()).ToArray();
    }
}

internal static class PublishJobMapping
{
    public static PublishJobDto ToDto(this PublishJob job)
    {
        return new PublishJobDto(
            job.Id,
            job.ApplicationId,
            job.SequenceNumber,
            job.Status.ToString(),
            job.OutputPath,
            job.CreatedUtc,
            job.CompletedUtc,
            job.FailureReason);
    }
}
