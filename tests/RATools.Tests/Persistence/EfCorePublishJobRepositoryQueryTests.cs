using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Publishing;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence;

public sealed class EfCorePublishJobRepositoryQueryTests
{
    [Fact]
    public async Task QueryHistoryAsync_LoadsStatusCountsAndPageWithTwoDatabaseCommands()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commandCounter = new CountingCommandInterceptor();
        await using var dbContext = await CreateDbContextAsync(connection, commandCounter);
        var repository = new EfCorePublishJobRepository(dbContext);
        var applicationId = Guid.NewGuid();
        var completed = new PublishJob(applicationId, "0001");
        completed.MarkRunning();
        completed.MarkCompleted("output", "package.zip");
        var failed = new PublishJob(applicationId, "0002");
        failed.MarkRunning();
        failed.MarkFailed("failed");
        var running = new PublishJob(applicationId, "0003");
        running.MarkRunning();

        await repository.AddAsync(completed);
        await repository.AddAsync(failed);
        await repository.AddAsync(running);
        await repository.AddAsync(new PublishJob(applicationId, "0004"));
        commandCounter.Reset();

        var result = await repository.QueryHistoryAsync(
            new PublishJobHistoryQuery(applicationId, null, null, null, null, 1, 20));

        Assert.Equal(4, result.TotalCount);
        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.RunningCount);
        Assert.Equal(2, commandCounter.CommandCount);
    }

    [Fact]
    public async Task QueryHistoryAsync_FiltersAndAggregatesMaterializedReadinessWithoutReadingReports()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commandCounter = new CountingCommandInterceptor();
        await using var dbContext = await CreateDbContextAsync(connection, commandCounter);
        var repository = new EfCorePublishJobRepository(dbContext);
        var applicationId = Guid.NewGuid();
        var ready = CompletedJob(applicationId, "0001");
        var blocked = CompletedJob(applicationId, "0002");
        var unknown = CompletedJob(applicationId, "0003");

        await repository.AddAsync(ready);
        await repository.AddAsync(blocked);
        await repository.AddAsync(unknown);
        Assert.False(await repository.UpdateHistorySummaryAsync(
            blocked.Id,
            blocked.AttemptCount + 1,
            Summary("Ready", isReady: true)));
        Assert.True(await repository.UpdateHistorySummaryAsync(
            ready.Id,
            ready.AttemptCount,
            Summary("Ready", isReady: true, matchedCount: 1)));
        Assert.True(await repository.UpdateHistorySummaryAsync(
            blocked.Id,
            blocked.AttemptCount,
            Summary("Blocked", isReady: false, replaceTargetNotFoundCount: 2)));
        commandCounter.Reset();

        var result = await repository.QueryHistoryAsync(
            new PublishJobHistoryQuery(
                applicationId,
                null,
                null,
                null,
                null,
                1,
                20,
                "blocked"));

        var item = Assert.Single(result.Items);
        Assert.Equal(blocked.Id, item.Id);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.CompletedCount);
        Assert.Equal(0, result.ReadinessCounts!.Ready);
        Assert.Equal(1, result.ReadinessCounts.Blocked);
        Assert.Equal(0, result.ReadinessCounts.Unknown);
        Assert.Equal(2, result.LifecycleCounts!.ReplaceTargetNotFound);
        Assert.Equal("Blocked", result.HistorySummaries![blocked.Id].ReadinessStatus);
        Assert.Equal(2, commandCounter.CommandCount);

        commandCounter.Reset();
        var unknownResult = await repository.QueryHistoryAsync(
            new PublishJobHistoryQuery(
                applicationId,
                null,
                null,
                null,
                null,
                1,
                20,
                "Unknown"));

        Assert.Equal(unknown.Id, Assert.Single(unknownResult.Items).Id);
        Assert.Equal(1, unknownResult.ReadinessCounts!.Unknown);
        Assert.Empty(unknownResult.HistorySummaries!);
        Assert.Equal(2, commandCounter.CommandCount);
    }

    private static PublishJob CompletedJob(Guid applicationId, string sequenceNumber)
    {
        var job = new PublishJob(applicationId, sequenceNumber);
        job.MarkRunning();
        job.MarkCompleted("output", "package.zip");
        return job;
    }

    private static PublishJobHistorySummary Summary(
        string readinessStatus,
        bool isReady,
        int matchedCount = 0,
        int replaceTargetNotFoundCount = 0)
        => new(
            ReportAvailable: true,
            ReportReadable: true,
            ReportError: null,
            ValidationProfile: "US FDA eCTD 3.2.2",
            ErrorCount: 0,
            WarningCount: 0,
            WarningSummary: null,
            ReadinessIsReady: isReady,
            ReadinessStatus: readinessStatus,
            ReadinessBlockingErrorCount: isReady ? 0 : 1,
            ReadinessWarningCount: 0,
            ReadinessMissingMetadataFields: isReady ? [] : ["ApplicantContactName"],
            LifecycleMatchedCount: matchedCount,
            LifecycleReplaceTargetNotFoundCount: replaceTargetNotFoundCount,
            LifecycleDeleteTargetNotFoundCount: 0,
            LifecycleAppendTargetNotFoundCount: 0,
            LifecycleAmbiguousCount: 0,
            LifecycleCurrentSequenceCount: 0,
            ArtifactFileCount: 7,
            ArtifactTotalSizeBytes: 4096,
            ArtifactPackageSizeBytes: 2048,
            ReportPath: "missing-on-purpose.json");

    private static async Task<RAToolsDbContext> CreateDbContextAsync(
        SqliteConnection connection,
        params IInterceptor[] interceptors)
    {
        var options = new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptors)
            .Options;
        var dbContext = new RAToolsDbContext(options);

        await dbContext.Database.EnsureCreatedAsync();

        return dbContext;
    }

    private sealed class CountingCommandInterceptor : DbCommandInterceptor
    {
        public int CommandCount { get; private set; }

        public void Reset()
        {
            CommandCount = 0;
        }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            CommandCount++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            CommandCount++;
            return ValueTask.FromResult(result);
        }

        public override InterceptionResult<object> ScalarExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result)
        {
            CommandCount++;
            return result;
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            CommandCount++;
            return ValueTask.FromResult(result);
        }
    }
}
