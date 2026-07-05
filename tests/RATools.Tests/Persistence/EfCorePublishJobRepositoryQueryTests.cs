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
