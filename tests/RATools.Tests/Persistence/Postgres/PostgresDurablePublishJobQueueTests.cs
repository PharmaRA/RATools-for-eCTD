using Microsoft.EntityFrameworkCore;
using RATools.Application.Publishing;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresDurablePublishJobQueueTests(PostgresFixture fixture)
{
    [RequiresPostgresFact]
    public async Task ConcurrentRepositoryInstancesClaimOnePendingJobOnlyOnce()
    {
        await ClearActiveJobsAsync();
        var job = new PublishJob(Guid.NewGuid(), "0001", $"postgres-claim-{Guid.NewGuid():N}");
        await using (var seedContext = fixture.CreateDbContext())
        {
            await new EfCorePublishJobRepository(seedContext).AddAsync(job);
        }

        var nowUtc = DateTime.UtcNow;
        await using var firstContext = fixture.CreateDbContext();
        await using var secondContext = fixture.CreateDbContext();
        var claims = await Task.WhenAll(
            new EfCorePublishJobRepository(firstContext).TryClaimNextAsync(
                "postgres-worker-a", nowUtc, TimeSpan.FromMinutes(1), 3),
            new EfCorePublishJobRepository(secondContext).TryClaimNextAsync(
                "postgres-worker-b", nowUtc, TimeSpan.FromMinutes(1), 3));

        var lease = Assert.Single(claims, claim => claim is not null)!;
        Assert.Equal(job.Id, lease.Job.Id);
        Assert.Equal(1, lease.Job.AttemptCount);
        await using var verifyContext = fixture.CreateDbContext();
        var stored = await verifyContext.PublishJobs.AsNoTracking().SingleAsync(record => record.Id == job.Id);
        Assert.Equal("Running", stored.Status);
        Assert.Equal(lease.Token, stored.LeaseToken);
        Assert.Equal(lease.Owner, stored.LeaseOwner);
    }

    [RequiresPostgresFact]
    public async Task IdempotencyAndFencingAreEnforcedAcrossRepositoryInstances()
    {
        await ClearActiveJobsAsync();
        var applicationId = Guid.NewGuid();
        var key = $"postgres-idempotency-{Guid.NewGuid():N}";
        await using var firstContext = fixture.CreateDbContext();
        await using var secondContext = fixture.CreateDbContext();
        var firstRepository = new EfCorePublishJobRepository(firstContext);
        var secondRepository = new EfCorePublishJobRepository(secondContext);

        var created = await firstRepository.AddOrGetByIdempotencyKeyAsync(
            new PublishJob(applicationId, "0001", key));
        var replay = await secondRepository.AddOrGetByIdempotencyKeyAsync(
            new PublishJob(applicationId, "0001", key));
        Assert.True(created.Created);
        Assert.False(replay.Created);
        Assert.Equal(created.Job.Id, replay.Job.Id);

        var nowUtc = DateTime.UtcNow;
        var lease = Assert.IsType<PublishJobLease>(await firstRepository.TryClaimNextAsync(
            "postgres-worker-a", nowUtc, TimeSpan.FromMinutes(1), 3));
        lease.Job.MarkCompleted("C:/publish/index.xml", "C:/publish/package.zip");

        Assert.False(await secondRepository.UpdateLeasedAsync(
            lease.Job, lease.Token, "postgres-worker-b", nowUtc.AddSeconds(1)));
        Assert.True(await firstRepository.UpdateLeasedAsync(
            lease.Job, lease.Token, lease.Owner, nowUtc.AddSeconds(1)));
    }

    private async Task ClearActiveJobsAsync()
    {
        await using var context = fixture.CreateDbContext();
        var nowUtc = DateTime.UtcNow;
        await context.PublishJobs
            .Where(job => job.Status == "Pending" || job.Status == "Running")
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(job => job.Status, "Failed")
                .SetProperty(job => job.CompletedUtc, nowUtc)
                .SetProperty(job => job.FailureReason, "Cleared by durable queue integration test.")
                .SetProperty(job => job.LeaseOwner, (string?)null)
                .SetProperty(job => job.LeaseToken, (Guid?)null)
                .SetProperty(job => job.LeaseExpiresUtc, (DateTime?)null)
                .SetProperty(job => job.LastHeartbeatUtc, (DateTime?)null));
    }
}
