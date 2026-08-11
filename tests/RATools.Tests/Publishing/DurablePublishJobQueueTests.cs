using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RATools.Application.Publishing;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Publishing;

public sealed class DurablePublishJobQueueTests
{
    [Fact]
    public async Task InMemory_IdempotentEnqueueReturnsOriginalJobAndRejectsDifferentRequest()
    {
        var repository = new InMemoryPublishJobRepository();
        var applicationId = Guid.NewGuid();
        const string key = "publish-request-0001";

        var first = await repository.AddOrGetByIdempotencyKeyAsync(new PublishJob(applicationId, "0001", key));
        var replay = await repository.AddOrGetByIdempotencyKeyAsync(new PublishJob(applicationId, "0001", key));

        Assert.True(first.Created);
        Assert.False(replay.Created);
        Assert.Equal(first.Job.Id, replay.Job.Id);
        await Assert.ThrowsAsync<PublishJobIdempotencyConflictException>(() =>
            repository.AddOrGetByIdempotencyKeyAsync(new PublishJob(applicationId, "0002", key)));
    }

    [Fact]
    public async Task InMemory_ConcurrentWorkersClaimAJobOnlyOnce()
    {
        var repository = new InMemoryPublishJobRepository();
        var job = new PublishJob(Guid.NewGuid(), "0001");
        await repository.AddAsync(job);
        var nowUtc = DateTime.UtcNow;

        var claims = await Task.WhenAll(Enumerable.Range(0, 16).Select(index => Task.Run(() =>
            repository.TryClaimNextAsync($"worker-{index}", nowUtc, TimeSpan.FromMinutes(1), 3))));

        var lease = Assert.Single(claims, claim => claim is not null)!;
        Assert.Equal(PublishJobStatus.Running, lease.Job.Status);
        Assert.Equal(1, lease.Job.AttemptCount);
        Assert.Equal(lease.Owner, (await repository.GetAsync(job.Id))!.LeaseOwner);
    }

    [Fact]
    public async Task InMemory_HeartbeatAndTerminalUpdateRequireCurrentOwnerAndToken()
    {
        var repository = new InMemoryPublishJobRepository();
        var job = new PublishJob(Guid.NewGuid(), "0001");
        await repository.AddAsync(job);
        var nowUtc = DateTime.UtcNow;
        var lease = Assert.IsType<PublishJobLease>(await repository.TryClaimNextAsync(
            "worker-a", nowUtc, TimeSpan.FromMinutes(1), 3));

        Assert.False(await repository.RenewLeaseAsync(
            job.Id, Guid.NewGuid(), lease.Owner, nowUtc.AddSeconds(10), TimeSpan.FromMinutes(1)));
        Assert.True(await repository.RenewLeaseAsync(
            job.Id, lease.Token, lease.Owner, nowUtc.AddSeconds(10), TimeSpan.FromMinutes(1)));

        lease.Job.MarkCompleted("C:/publish/index.xml", "C:/publish/package.zip");
        Assert.False(await repository.UpdateLeasedAsync(
            lease.Job, Guid.NewGuid(), lease.Owner, nowUtc.AddSeconds(20)));
        Assert.True(await repository.UpdateLeasedAsync(
            lease.Job, lease.Token, lease.Owner, nowUtc.AddSeconds(20)));
        Assert.Equal(PublishJobStatus.Completed, (await repository.GetAsync(job.Id))!.Status);
    }

    [Fact]
    public async Task InMemory_RetryDelayAndMaximumAttemptsArePersisted()
    {
        var repository = new InMemoryPublishJobRepository();
        var job = new PublishJob(Guid.NewGuid(), "0001");
        await repository.AddAsync(job);
        var nowUtc = DateTime.UtcNow;
        var firstLease = Assert.IsType<PublishJobLease>(await repository.TryClaimNextAsync(
            "worker-a", nowUtc, TimeSpan.FromMinutes(1), 2));
        var retryAt = nowUtc.AddMinutes(2);

        var retry = await repository.RetryOrFailLeasedAsync(
            job.Id, firstLease.Token, firstLease.Owner, nowUtc, retryAt, 2, "Transient failure");

        Assert.Equal(PublishJobRetryDisposition.RetryScheduled, retry.Disposition);
        Assert.Equal(retryAt, retry.Job!.NextAttemptUtc);
        Assert.Null(await repository.TryClaimNextAsync(
            "worker-b", nowUtc.AddMinutes(1), TimeSpan.FromMinutes(1), 2));

        var secondLease = Assert.IsType<PublishJobLease>(await repository.TryClaimNextAsync(
            "worker-b", retryAt, TimeSpan.FromMinutes(1), 2));
        var exhausted = await repository.RetryOrFailLeasedAsync(
            job.Id, secondLease.Token, secondLease.Owner, retryAt, retryAt, 2, "Still failing");

        Assert.Equal(PublishJobRetryDisposition.Failed, exhausted.Disposition);
        Assert.Equal(2, exhausted.Job!.AttemptCount);
        Assert.Equal(PublishJobStatus.Failed, exhausted.Job.Status);
        Assert.Null(exhausted.Job.LeaseToken);
    }

    [Fact]
    public async Task EfCore_ClaimsAndFencesWithDatabaseState()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RAToolsDbContext>().UseSqlite(connection).Options;
        await using var firstContext = new RAToolsDbContext(options);
        await firstContext.Database.EnsureCreatedAsync();
        var firstRepository = new EfCorePublishJobRepository(firstContext);
        var job = new PublishJob(Guid.NewGuid(), "0001", "ef-claim-0001");
        await firstRepository.AddAsync(job);
        var nowUtc = DateTime.UtcNow;

        var lease = Assert.IsType<PublishJobLease>(await firstRepository.TryClaimNextAsync(
            "worker-a", nowUtc, TimeSpan.FromMinutes(1), 3));
        await using var secondContext = new RAToolsDbContext(options);
        var secondRepository = new EfCorePublishJobRepository(secondContext);

        Assert.Null(await secondRepository.TryClaimNextAsync(
            "worker-b", nowUtc, TimeSpan.FromMinutes(1), 3));
        Assert.False(await secondRepository.RenewLeaseAsync(
            job.Id, Guid.NewGuid(), "worker-a", nowUtc.AddSeconds(10), TimeSpan.FromMinutes(1)));
        Assert.True(await secondRepository.RenewLeaseAsync(
            job.Id, lease.Token, lease.Owner, nowUtc.AddSeconds(10), TimeSpan.FromMinutes(1)));

        lease.Job.MarkCompleted("C:/publish/index.xml", "C:/publish/package.zip");
        Assert.False(await firstRepository.UpdateLeasedAsync(
            lease.Job, lease.Token, "worker-b", nowUtc.AddSeconds(20)));
        Assert.True(await firstRepository.UpdateLeasedAsync(
            lease.Job, lease.Token, lease.Owner, nowUtc.AddSeconds(20)));
        Assert.Equal(PublishJobStatus.Completed, (await secondRepository.GetAsync(job.Id))!.Status);
    }

    [Fact]
    public async Task EfCore_IdempotentEnqueueAndRetryMetadataRoundTrip()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RAToolsDbContext>().UseSqlite(connection).Options;
        await using var context = new RAToolsDbContext(options);
        await context.Database.EnsureCreatedAsync();
        var repository = new EfCorePublishJobRepository(context);
        var applicationId = Guid.NewGuid();
        const string key = "ef-idempotency-0001";

        var first = await repository.AddOrGetByIdempotencyKeyAsync(new PublishJob(applicationId, "0001", key));
        var replay = await repository.AddOrGetByIdempotencyKeyAsync(new PublishJob(applicationId, "0001", key));
        Assert.True(first.Created);
        Assert.False(replay.Created);
        Assert.Equal(first.Job.Id, replay.Job.Id);

        var nowUtc = DateTime.UtcNow;
        var lease = Assert.IsType<PublishJobLease>(await repository.TryClaimNextAsync(
            "worker-a", nowUtc, TimeSpan.FromMinutes(1), 3));
        var retryAt = nowUtc.AddMinutes(1);
        var retry = await repository.RetryOrFailLeasedAsync(
            lease.Job.Id, lease.Token, lease.Owner, nowUtc, retryAt, 3, "Transient database test failure");

        Assert.Equal(PublishJobRetryDisposition.RetryScheduled, retry.Disposition);
        Assert.Equal(1, retry.Job!.AttemptCount);
        Assert.Equal(retryAt, retry.Job.NextAttemptUtc);
        Assert.Null(retry.Job.LeaseToken);
    }
}
