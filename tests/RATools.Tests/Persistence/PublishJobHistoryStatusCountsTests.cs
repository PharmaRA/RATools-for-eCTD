using RATools.Domain.Publishing;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Persistence;

public sealed class PublishJobHistoryStatusCountsTests
{
    [Fact]
    public void Create_CountsStatusesInOneSummary()
    {
        var applicationId = Guid.NewGuid();
        var jobs = new[]
        {
            Job(applicationId, PublishJobStatus.Completed),
            Job(applicationId, PublishJobStatus.Completed),
            Job(applicationId, PublishJobStatus.Failed),
            Job(applicationId, PublishJobStatus.Running),
            Job(applicationId, PublishJobStatus.Pending),
        };

        var counts = PublishJobHistoryStatusCounts.Create(jobs);

        Assert.Equal(5, counts.Total);
        Assert.Equal(2, counts.Completed);
        Assert.Equal(1, counts.Failed);
        Assert.Equal(1, counts.Running);
    }

    private static PublishJob Job(Guid applicationId, PublishJobStatus status)
        => PublishJob.Rehydrate(
            Guid.NewGuid(),
            applicationId,
            "0001",
            status,
            null,
            null,
            DateTime.UtcNow,
            null,
            null);
}
