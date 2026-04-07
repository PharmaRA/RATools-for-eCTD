using System.Text.Json;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Validation.Dtos;
using RATools.Domain.Applications;
using RATools.Domain.Publishing;
using Xunit;

namespace RATools.Application.Tests.Applications;

public sealed class ApplicationPublishHistoryServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsApplicationHistoryOrderedByNewestPublishJob()
    {
        var application = SubmissionApplication.Rehydrate(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "IND-0001",
            "US",
            "Demo Sponsor",
            DateTime.UtcNow.AddDays(-10),
            [SubmissionSequence.Rehydrate("0000", "original-application", "Initial", DateTime.UtcNow.AddDays(-9))]);

        var tempRoot = Path.Combine(Path.GetTempPath(), "ratools-history-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var reportPath = Path.Combine(tempRoot, "publish-report-0000-00000000000000000000000000000002.json");
            await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(new PublishExecutionReportDto(
                "1.1",
                application.Id,
                "0000",
                "default-v1",
                reportPath,
                new ValidationReportDto(application.Id, "0000", "default-v1", true, Array.Empty<ValidationIssueDto>()),
                new PublishJobDto(
                    Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    application.Id,
                    "0000",
                    "Completed",
                    Path.Combine(tempRoot, "index.xml"),
                    Path.Combine(tempRoot, "0000.zip"),
                    DateTime.UtcNow.AddMinutes(-2),
                    DateTime.UtcNow.AddMinutes(-1),
                    null),
                100,
                new PublishArtifactSummaryDto(3, 1024, 512),
                null,
                0,
                1,
                "TITLE_MISSING",
                true,
                "ok"), new JsonSerializerOptions { WriteIndented = true }));

            var publishJob = PublishJob.Rehydrate(
                Guid.Parse("00000000-0000-0000-0000-000000000002"),
                application.Id,
                "0000",
                PublishJobStatus.Completed,
                Path.Combine(tempRoot, "index.xml"),
                Path.Combine(tempRoot, "0000.zip"),
                DateTime.UtcNow.AddMinutes(-2),
                DateTime.UtcNow.AddMinutes(-1),
                null);

            var service = new ApplicationPublishHistoryService(
                new StubApplicationRepository(application),
                new StubPublishJobRepository(publishJob));

            var result = await service.GetAsync(application.Id, new ApplicationPublishHistoryQuery(null, 1, 20));

            Assert.NotNull(result);
            Assert.Equal(application.Id, result!.ApplicationId);
        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.PageSize);
        Assert.Equal(1, result.TotalCount);
        Assert.NotNull(result.StatusSummary);
        Assert.Equal(1, result.StatusSummary.CompletedCount);
        Assert.Equal(0, result.StatusSummary.FailedCount);
        Assert.Equal(0, result.StatusSummary.RunningCount);
        Assert.Single(result.Entries);
            var entry = result.Entries.First();
            Assert.True(entry.ReportAvailable);
            Assert.Equal("default-v1", entry.ValidationProfile);
            Assert.Equal(1, entry.WarningCount);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public async Task GetAsync_AppliesSequenceFilterAndPagination()
    {
        var application = SubmissionApplication.Rehydrate(
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            "IND-0002",
            "US",
            "Demo Sponsor",
            DateTime.UtcNow.AddDays(-10),
            [
                SubmissionSequence.Rehydrate("0000", "original-application", "Initial", DateTime.UtcNow.AddDays(-9)),
                SubmissionSequence.Rehydrate("0001", "amendment", "Update", DateTime.UtcNow.AddDays(-8))
            ]);

        var jobs = new[]
        {
            PublishJob.Rehydrate(Guid.Parse("10000000-0000-0000-0000-000000000011"), application.Id, "0000", PublishJobStatus.Completed, null, null, DateTime.UtcNow.AddDays(-3), DateTime.UtcNow.AddDays(-3).AddMinutes(1), null),
            PublishJob.Rehydrate(Guid.Parse("10000000-0000-0000-0000-000000000012"), application.Id, "0001", PublishJobStatus.Completed, null, null, DateTime.UtcNow.AddDays(-2), DateTime.UtcNow.AddDays(-2).AddMinutes(1), null),
            PublishJob.Rehydrate(Guid.Parse("10000000-0000-0000-0000-000000000013"), application.Id, "0001", PublishJobStatus.Completed, null, null, DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(-1).AddMinutes(1), null)
        };

        var service = new ApplicationPublishHistoryService(
            new StubApplicationRepository(application),
            new StubPublishJobRepository(jobs));

        var result = await service.GetAsync(application.Id, new ApplicationPublishHistoryQuery("0001", 1, 1));

        Assert.NotNull(result);
        Assert.Equal(2, result!.TotalCount);
        Assert.Equal(2, result.StatusSummary.CompletedCount);
        Assert.Equal(1, result.Page);
        Assert.Equal(1, result.PageSize);
        Assert.Single(result.Entries);
        Assert.Equal("0001", result.Entries.First().SequenceNumber);
        Assert.Equal(Guid.Parse("10000000-0000-0000-0000-000000000013"), result.Entries.First().PublishJobId);
    }

    [Fact]
    public async Task GetAsync_AppliesStatusAndCreatedUtcRangeFilters()
    {
        var application = SubmissionApplication.Rehydrate(
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            "IND-0003",
            "US",
            "Demo Sponsor",
            DateTime.UtcNow.AddDays(-10),
            [SubmissionSequence.Rehydrate("0000", "original-application", "Initial", DateTime.UtcNow.AddDays(-9))]);

        var jobs = new[]
        {
            PublishJob.Rehydrate(Guid.Parse("20000000-0000-0000-0000-000000000011"), application.Id, "0000", PublishJobStatus.Completed, null, null, new DateTime(2026, 4, 1, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 1, 8, 1, 0, DateTimeKind.Utc), null),
            PublishJob.Rehydrate(Guid.Parse("20000000-0000-0000-0000-000000000012"), application.Id, "0000", PublishJobStatus.Failed, null, null, new DateTime(2026, 4, 2, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 2, 8, 1, 0, DateTimeKind.Utc), "x"),
            PublishJob.Rehydrate(Guid.Parse("20000000-0000-0000-0000-000000000013"), application.Id, "0000", PublishJobStatus.Completed, null, null, new DateTime(2026, 4, 3, 8, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 3, 8, 1, 0, DateTimeKind.Utc), null)
        };

        var service = new ApplicationPublishHistoryService(
            new StubApplicationRepository(application),
            new StubPublishJobRepository(jobs));

        var result = await service.GetAsync(
            application.Id,
            new ApplicationPublishHistoryQuery(
                null,
                1,
                20,
                "Completed",
                new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 4, 3, 23, 59, 59, DateTimeKind.Utc)));

        Assert.NotNull(result);
        Assert.Equal(1, result!.TotalCount);
        Assert.Equal(1, result.StatusSummary.CompletedCount);
        Assert.Equal(0, result.StatusSummary.FailedCount);
        Assert.Equal(0, result.StatusSummary.RunningCount);
        Assert.Single(result.Entries);
        Assert.Equal(Guid.Parse("20000000-0000-0000-0000-000000000013"), result.Entries.First().PublishJobId);
        Assert.Equal("Completed", result.Entries.First().Status);
    }

    [Fact]
    public async Task GetAsync_ToleratesCorruptedPersistedReport()
    {
        var application = SubmissionApplication.Rehydrate(
            Guid.Parse("40000000-0000-0000-0000-000000000001"),
            "IND-0005",
            "US",
            "Demo Sponsor",
            DateTime.UtcNow.AddDays(-10),
            [SubmissionSequence.Rehydrate("0000", "original-application", "Initial", DateTime.UtcNow.AddDays(-9))]);

        var tempRoot = Path.Combine(Path.GetTempPath(), "ratools-history-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var reportPath = Path.Combine(tempRoot, "publish-report-0000-40000000000000000000000000000002.json");
            await File.WriteAllTextAsync(reportPath, "{not-json}");

            var publishJob = PublishJob.Rehydrate(
                Guid.Parse("40000000-0000-0000-0000-000000000002"),
                application.Id,
                "0000",
                PublishJobStatus.Completed,
                Path.Combine(tempRoot, "index.xml"),
                Path.Combine(tempRoot, "0000.zip"),
                DateTime.UtcNow.AddMinutes(-2),
                DateTime.UtcNow.AddMinutes(-1),
                null);

            var service = new ApplicationPublishHistoryService(
                new StubApplicationRepository(application),
                new StubPublishJobRepository(publishJob));

            var result = await service.GetAsync(application.Id, new ApplicationPublishHistoryQuery(null, 1, 20));

            Assert.NotNull(result);
            var entry = result!.Entries.Single();
            Assert.True(entry.ReportAvailable);
            Assert.False(entry.ReportReadable);
            Assert.False(string.IsNullOrWhiteSpace(entry.ReportError));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, true);
            }
        }
    }

    [Fact]
    public async Task GetAsync_UsesRepositoryLevelHistoryQuery()
    {
        var application = SubmissionApplication.Rehydrate(
            Guid.Parse("50000000-0000-0000-0000-000000000001"),
            "IND-0006",
            "US",
            "Demo Sponsor",
            DateTime.UtcNow,
            [SubmissionSequence.Rehydrate("0000", "original-application", "Initial", DateTime.UtcNow)]);

        var expectedJob = PublishJob.Rehydrate(
            Guid.Parse("50000000-0000-0000-0000-000000000002"),
            application.Id,
            "0000",
            PublishJobStatus.Completed,
            null,
            null,
            DateTime.UtcNow,
            DateTime.UtcNow,
            null);

        var queryAwareRepository = new QueryOnlyPublishJobRepository(expectedJob);
        var service = new ApplicationPublishHistoryService(
            new StubApplicationRepository(application),
            queryAwareRepository);

        var result = await service.GetAsync(application.Id, new ApplicationPublishHistoryQuery("0000", 1, 20));

        Assert.NotNull(result);
        Assert.Single(result!.Entries);
        Assert.Equal(expectedJob.Id, result.Entries.First().PublishJobId);
    }
}

file sealed class StubApplicationRepository(SubmissionApplication application) : IApplicationRepository
{
    public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(id == application.Id ? application : null);

    public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult((IReadOnlyCollection<SubmissionApplication>)[application]);
}

file sealed class StubPublishJobRepository(params PublishJob[] jobs) : IPublishJobRepository
{
    public Task AddAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default)
        => Task.FromResult(jobs.SingleOrDefault(x => x.Id == id));

    public Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default)
        => Task.FromResult((IReadOnlyCollection<PublishJob>)jobs);

    public Task<PublishJobHistoryQueryResult> QueryHistoryAsync(PublishJobHistoryQuery query, CancellationToken cancellationToken = default)
    {
        var filtered = jobs
            .Where(x => x.ApplicationId == query.ApplicationId)
            .Where(x => string.IsNullOrWhiteSpace(query.SequenceNumber) || x.SequenceNumber == query.SequenceNumber)
            .Where(x => string.IsNullOrWhiteSpace(query.Status) || x.Status.ToString().Equals(query.Status, StringComparison.OrdinalIgnoreCase))
            .Where(x => !query.CreatedFromUtc.HasValue || x.CreatedUtc >= query.CreatedFromUtc.Value)
            .Where(x => !query.CreatedToUtc.HasValue || x.CreatedUtc <= query.CreatedToUtc.Value)
            .OrderByDescending(x => x.CreatedUtc)
            .ToArray();

        var pageItems = filtered.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToArray();
        return Task.FromResult(new PublishJobHistoryQueryResult(
            pageItems,
            filtered.Length,
            filtered.Count(x => x.Status == PublishJobStatus.Completed),
            filtered.Count(x => x.Status == PublishJobStatus.Failed),
            filtered.Count(x => x.Status == PublishJobStatus.Running)));
    }
}

file sealed class QueryOnlyPublishJobRepository(PublishJob expectedJob) : IPublishJobRepository
{
    public Task AddAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult(id == expectedJob.Id ? expectedJob : null);
    public Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("ListAsync should not be used for publish history anymore.");
    public Task<PublishJobHistoryQueryResult> QueryHistoryAsync(PublishJobHistoryQuery query, CancellationToken cancellationToken = default)
        => Task.FromResult(new PublishJobHistoryQueryResult([expectedJob], 1, 1, 0, 0));
}
