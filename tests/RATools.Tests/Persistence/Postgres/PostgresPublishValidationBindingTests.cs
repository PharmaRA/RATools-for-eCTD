using Microsoft.EntityFrameworkCore;
using RATools.Application.Publishing;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresPublishValidationBindingTests
{
    [RequiresPostgresFact]
    public async Task RealClaimAndRetryReloadKeepTheExplicitValidationRun()
    {
        var now = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var runId = Guid.NewGuid();
        var job = PublishJob.Rehydrate(Guid.NewGuid(), Guid.NewGuid(), "0001", PublishJobStatus.Pending,
            null, null, now.AddMinutes(-1), null, null, idempotencyKey: $"binding-claim-{Guid.NewGuid():N}",
            packageValidationRunId: runId);
        await using (var seed = CreateContext())
            await new EfCorePublishJobRepository(seed).AddAsync(job);
        try
        {
            await using var first = CreateContext();
            var repository = new EfCorePublishJobRepository(first);
            await EnsureOnlyOwnedCandidateAsync(first, job.Id, now);
            var lease = Assert.IsType<PublishJobLease>(await repository.TryClaimNextAsync(
                "binding-first", now, TimeSpan.FromMinutes(1), 3));
            Assert.Equal(job.Id, lease.Job.Id);
            Assert.Equal(runId, lease.Job.PackageValidationRunId);
            var retry = await repository.RetryOrFailLeasedAsync(job.Id, lease.Token, lease.Owner,
                now.AddSeconds(1), now.AddSeconds(2), 3, "Synthetic retry before artifact generation.");
            Assert.Equal(PublishJobRetryDisposition.RetryScheduled, retry.Disposition);
            Assert.Equal(runId, retry.Job!.PackageValidationRunId);

            await using var restarted = CreateContext();
            var restartedRepository = new EfCorePublishJobRepository(restarted);
            await EnsureOnlyOwnedCandidateAsync(restarted, job.Id, now.AddSeconds(3));
            var reclaimed = Assert.IsType<PublishJobLease>(await restartedRepository.TryClaimNextAsync(
                "binding-restarted", now.AddSeconds(3), TimeSpan.FromMinutes(1), 3));
            Assert.Equal(job.Id, reclaimed.Job.Id);
            Assert.Equal(2, reclaimed.Job.AttemptCount);
            Assert.NotEqual(lease.Token, reclaimed.Token);
            Assert.Equal(runId, reclaimed.Job.PackageValidationRunId);
            Assert.False(await repository.RenewLeaseAsync(job.Id, lease.Token, lease.Owner,
                now.AddSeconds(4), TimeSpan.FromMinutes(1)));
            Assert.Equal(runId, (await restartedRepository.GetAsync(job.Id))!.PackageValidationRunId);
        }
        finally { await FinishOwnedJobAsync(job.Id); }
    }

    [RequiresPostgresFact]
    public async Task ConcurrentIdenticalRequestsReplayOnePersistedBinding()
    {
        var applicationId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var key = $"binding-replay-{Guid.NewGuid():N}";
        await using var first = CreateContext();
        await using var second = CreateContext();
        try
        {
            var results = await Task.WhenAll(
                new EfCorePublishJobRepository(first).AddOrGetByIdempotencyKeyAsync(new(applicationId, "0001", key, runId)),
                new EfCorePublishJobRepository(second).AddOrGetByIdempotencyKeyAsync(new(applicationId, "0001", key, runId)));
            Assert.Single(results, result => result.Created);
            Assert.Equal(results[0].Job.Id, results[1].Job.Id);
            Assert.All(results, result => Assert.Equal(runId, result.Job.PackageValidationRunId));
            await using var verify = CreateContext();
            var stored = Assert.Single(await verify.PublishJobs.AsNoTracking().Where(row => row.IdempotencyKey == key).ToArrayAsync());
            Assert.Equal(runId, stored.PackageValidationRunId);
        }
        finally { await FinishOwnedKeyAsync(key); }
    }

    [RequiresPostgresFact]
    public async Task UniqueConstraintReplayCannotChangeOrRemoveTheBinding()
    {
        var applicationId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var key = $"binding-conflict-{Guid.NewGuid():N}";
        await using (var seed = CreateContext())
            await new EfCorePublishJobRepository(seed).AddOrGetByIdempotencyKeyAsync(new(applicationId, "0001", key, runId));
        try
        {
            foreach (var changedRun in new Guid?[] { Guid.NewGuid(), null })
            {
                await using var contender = CreateContext();
                await Assert.ThrowsAsync<PublishJobIdempotencyConflictException>(() =>
                    new EfCorePublishJobRepository(contender).AddOrGetByIdempotencyKeyAsync(
                        new(applicationId, "0001", key, changedRun)));
                Assert.Empty(contender.ChangeTracker.Entries<PublishJobRecord>());
            }
            await using var verify = CreateContext();
            var stored = Assert.Single(await verify.PublishJobs.AsNoTracking().Where(row => row.IdempotencyKey == key).ToArrayAsync());
            Assert.Equal(runId, stored.PackageValidationRunId);
        }
        finally { await FinishOwnedKeyAsync(key); }
    }

    private static RAToolsDbContext CreateContext() => new PostgresFixture().CreateDbContext();

    // This collection runs serially against a dedicated test database, without
    // application workers. Refuse old interrupted-test residue before claim can
    // mutate it; a past timestamp by itself does not provide isolation.
    private static async Task EnsureOnlyOwnedCandidateAsync(RAToolsDbContext context, Guid id, DateTime now)
    {
        var unrelatedCandidate = await context.PublishJobs.AsNoTracking().AnyAsync(row =>
            row.Id != id && row.Status == "Pending" && row.NextAttemptUtc <= now && row.AttemptCount < 3);
        Assert.False(unrelatedCandidate,
            "The dedicated test database contains another eligible task; refusing to claim unrelated work.");
    }

    private static async Task FinishOwnedKeyAsync(string key)
    {
        await using var context = CreateContext();
        var id = await context.PublishJobs.Where(row => row.IdempotencyKey == key).Select(row => (Guid?)row.Id).SingleOrDefaultAsync();
        if (id.HasValue) await FinishOwnedJobAsync(id.Value);
    }

    private static async Task FinishOwnedJobAsync(Guid id)
    {
        await using var context = CreateContext();
        await context.PublishJobs.Where(row => row.Id == id && (row.Status == "Pending" || row.Status == "Running"))
            .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, "Failed")
                .SetProperty(row => row.CompletedUtc, DateTime.UtcNow)
                .SetProperty(row => row.FailureReason, "Completed binding regression; no artifact generation.")
                .SetProperty(row => row.LeaseOwner, (string?)null)
                .SetProperty(row => row.LeaseToken, (Guid?)null)
                .SetProperty(row => row.LeaseExpiresUtc, (DateTime?)null)
                .SetProperty(row => row.LastHeartbeatUtc, (DateTime?)null));
    }
}
