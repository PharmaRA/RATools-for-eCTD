using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RATools.Application.Publishing;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Persistence.EfCore;

namespace RATools.Tests.Persistence;

public sealed class EfCorePublishJobRepositoryTests
{
    [Fact]
    public async Task AddAsync_ThrowsPublishJobAlreadyInProgressException_WhenSecondActiveJobCollides()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var repository = new EfCorePublishJobRepository(dbContext);
        var applicationId = Guid.NewGuid();

        await repository.AddAsync(new PublishJob(applicationId, "0001"));

        await Assert.ThrowsAsync<PublishJobAlreadyInProgressException>(() =>
            repository.AddAsync(new PublishJob(applicationId, "0001")));
    }

    [Fact]
    public async Task AddAsync_AllowsCompletedJobHistoryForSameApplicationAndSequence()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var dbContext = await CreateDbContextAsync(connection);
        var repository = new EfCorePublishJobRepository(dbContext);
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

    private static async Task<RAToolsDbContext> CreateDbContextAsync(SqliteConnection connection)
    {
        var options = new DbContextOptionsBuilder<RAToolsDbContext>()
            .UseSqlite(connection)
            .Options;
        var dbContext = new RAToolsDbContext(options);

        await dbContext.Database.EnsureCreatedAsync();

        return dbContext;
    }
}
