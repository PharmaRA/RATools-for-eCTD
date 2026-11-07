using RATools.Application.Abstractions.Persistence;
using RATools.Application.Publishing;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Persistence.EfCore;
using RATools.Tests.Publishing;

namespace RATools.Tests.Persistence.Postgres;

[Collection(PostgresCollectionDefinition.Name)]
public sealed class PostgresPublishCompletionReportTests(PostgresFixture fixture)
{
    [RequiresPostgresFact]
    public async Task CompletedAttemptHasAReadableCompleteReportAfterANewDatabaseContext()
        => await VerifyAsync(false);

    [RequiresPostgresFact]
    public async Task CommittedTerminalResponseLossReplaysAfterPostgresTimestampRoundTrip()
        => await VerifyAsync(true);

    private async Task VerifyAsync(bool loseResponse)
    {
        await using var context = fixture.CreateDbContext();
        var inner = new EfCorePublishJobRepository(context);
        IPublishJobRepository repository = loseResponse ? new LostResponseRepository(inner) : inner;
        var before = (await inner.ListAsync()).Select(job => job.Id).ToHashSet();
        await PublishJobTerminalPersistenceTests.VerifyPersistedCompletedReportAsync(repository, async () =>
        {
            await using var reload = fixture.CreateDbContext();
            var jobs = await new EfCorePublishJobRepository(reload).ListAsync();
            return Assert.Single(jobs, job => !before.Contains(job.Id));
        });
    }

    private sealed class LostResponseRepository(IPublishJobRepository inner) : IPublishJobRepository
    {
        public Task AddAsync(PublishJob job, CancellationToken cancellationToken = default) => inner.AddAsync(job, cancellationToken);
        public Task<PublishJobEnqueueResult> AddOrGetByIdempotencyKeyAsync(PublishJob job, CancellationToken cancellationToken = default) => inner.AddOrGetByIdempotencyKeyAsync(job, cancellationToken);
        public Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default) => inner.UpdateAsync(job, cancellationToken);
        public Task<bool> UpdateHistorySummaryAsync(Guid jobId, int expectedAttemptCount, PublishJobHistorySummary summary, CancellationToken cancellationToken = default) => inner.UpdateHistorySummaryAsync(jobId, expectedAttemptCount, summary, cancellationToken);
        public Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetAsync(id, cancellationToken);
        public Task<PublishJob?> GetByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default) => inner.GetByIdempotencyKeyAsync(key, cancellationToken);
        public Task<PublishJobLease?> TryClaimNextAsync(string owner, DateTime nowUtc, TimeSpan leaseDuration, int maxAttempts, CancellationToken cancellationToken = default) => inner.TryClaimNextAsync(owner, nowUtc, leaseDuration, maxAttempts, cancellationToken);
        public async Task<bool> UpdateLeasedAsync(PublishJob job, Guid token, string owner, DateTime nowUtc, CancellationToken cancellationToken = default)
        {
            var updated = await inner.UpdateLeasedAsync(job, token, owner, nowUtc, cancellationToken);
            if (updated) throw new IOException("Database committed; terminal response was lost.");
            return false;
        }
        public Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default) => inner.ListAsync(cancellationToken);
        public Task<IReadOnlyCollection<PublishJob>> ListActiveAsync(CancellationToken cancellationToken = default) => inner.ListActiveAsync(cancellationToken);
        public Task<PublishJobHistoryQueryResult> QueryHistoryAsync(PublishJobHistoryQuery query, CancellationToken cancellationToken = default) => inner.QueryHistoryAsync(query, cancellationToken);
    }
}
