using RATools.Application.Publishing;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Persistence;

public sealed class InMemoryPublishJobRepositoryTests
{
    [Fact]
    public async Task AddAsync_ThrowsPublishJobAlreadyInProgressException_WhenSecondActiveJobCollides()
    {
        var repository = new InMemoryPublishJobRepository();
        var applicationId = Guid.NewGuid();

        await repository.AddAsync(new PublishJob(applicationId, "0001"));

        await Assert.ThrowsAsync<PublishJobAlreadyInProgressException>(() =>
            repository.AddAsync(new PublishJob(applicationId, "0001")));
    }

    [Fact]
    public async Task AddAsync_AllowsCompletedJobHistoryForSameApplicationAndSequence()
    {
        var repository = new InMemoryPublishJobRepository();
        var applicationId = Guid.NewGuid();
        var firstJob = new PublishJob(applicationId, "0001");
        var secondJob = new PublishJob(applicationId, "0001");

        firstJob.MarkRunning();
        firstJob.MarkCompleted("output", "package.zip");
        secondJob.MarkRunning();
        secondJob.MarkCompleted("output-2", "package-2.zip");

        await repository.AddAsync(firstJob);
        await repository.AddAsync(secondJob);

        var jobs = await repository.ListAsync();
        Assert.Equal(2, jobs.Count);
    }

    [Fact]
    public async Task AddAsync_AllowsActiveJobForDifferentSequence()
    {
        var repository = new InMemoryPublishJobRepository();
        var applicationId = Guid.NewGuid();

        await repository.AddAsync(new PublishJob(applicationId, "0001"));
        await repository.AddAsync(new PublishJob(applicationId, "0002"));

        var jobs = await repository.ListAsync();
        Assert.Equal(2, jobs.Count);
    }

    [Fact]
    public async Task AddAsync_AllowsOnlyOneActiveJob_UnderConcurrentRequests()
    {
        var repository = new InMemoryPublishJobRepository();
        var applicationId = Guid.NewGuid();
        const int attempts = 32;

        var tasks = Enumerable.Range(0, attempts)
            .Select(_ => Task.Run(async () =>
            {
                try
                {
                    await repository.AddAsync(new PublishJob(applicationId, "0001"));
                    return true;
                }
                catch (PublishJobAlreadyInProgressException)
                {
                    return false;
                }
            }))
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.Equal(1, results.Count(succeeded => succeeded));
        var jobs = await repository.ListAsync();
        Assert.Single(jobs);
    }

}
