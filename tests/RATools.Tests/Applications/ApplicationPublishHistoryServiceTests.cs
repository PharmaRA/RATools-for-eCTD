using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications;
using RATools.Domain.Applications;
using RATools.Domain.Publishing;

namespace RATools.Tests.Applications;

public sealed class ApplicationPublishHistoryServiceTests
{
    [Fact]
    public async Task GetAsync_MapsMaterializedSummaryWithoutReadingReportFile()
    {
        var applicationId = Guid.NewGuid();
        var job = CompletedJob(applicationId, "0001");
        var summary = Summary("Blocked", isReady: false);
        var repository = new StubPublishJobRepository(new PublishJobHistoryQueryResult(
            [job],
            TotalCount: 1,
            CompletedCount: 1,
            FailedCount: 0,
            RunningCount: 0,
            HistorySummaries: new Dictionary<Guid, PublishJobHistorySummary> { [job.Id] = summary },
            ReadinessCounts: new PublishJobHistoryReadinessCounts(Ready: 0, Blocked: 1, Unknown: 0),
            LifecycleCounts: new PublishJobHistoryLifecycleCounts(
                Matched: 1,
                ReplaceTargetNotFound: 2,
                DeleteTargetNotFound: 0,
                AppendTargetNotFound: 0,
                Ambiguous: 0,
                CurrentSequence: 0)));
        var service = new ApplicationPublishHistoryService(
            new StubApplicationRepository(Application(applicationId)),
            repository);

        var history = await service.GetAsync(
            applicationId,
            new ApplicationPublishHistoryQuery(null, 1, 20, null, null, null, null));

        var entry = Assert.Single(history!.Entries);
        Assert.True(entry.ReportAvailable);
        Assert.True(entry.ReportReadable);
        Assert.Equal("US FDA eCTD 3.2.2", entry.ValidationProfile);
        Assert.Equal(3, entry.WarningCount);
        Assert.Equal("Blocked", entry.PublishReadiness!.Status);
        Assert.False(entry.PublishReadiness.IsReady);
        Assert.Equal(["ApplicantContactName"], entry.PublishReadiness.MissingMetadataFields);
        Assert.Equal(1, entry.LifecycleSummary.MatchedCount);
        Assert.Equal(2, entry.LifecycleSummary.ReplaceTargetNotFoundCount);
        Assert.Empty(entry.LifecycleMatches);
        Assert.Equal(7, entry.ArtifactSummary!.FileCount);
        Assert.Equal("Z:/report-does-not-exist.json", entry.ReportPath);
        Assert.Equal(1, history.ReadinessSummary.BlockedCount);
        Assert.Equal(2, history.LifecycleSummary.ReplaceTargetNotFoundCount);
    }

    [Fact]
    public async Task GetAsync_PushesFiltersAndPaginationIntoRepository()
    {
        var applicationId = Guid.NewGuid();
        var repository = new StubPublishJobRepository(new PublishJobHistoryQueryResult(
            [],
            TotalCount: 0,
            CompletedCount: 0,
            FailedCount: 0,
            RunningCount: 0,
            HistorySummaries: new Dictionary<Guid, PublishJobHistorySummary>(),
            ReadinessCounts: new PublishJobHistoryReadinessCounts(0, 0, 0),
            LifecycleCounts: new PublishJobHistoryLifecycleCounts(0, 0, 0, 0, 0, 0)));
        var service = new ApplicationPublishHistoryService(
            new StubApplicationRepository(Application(applicationId)),
            repository);
        var createdFrom = DateTime.UtcNow.AddDays(-2);
        var createdTo = DateTime.UtcNow.AddDays(-1);

        var history = await service.GetAsync(
            applicationId,
            new ApplicationPublishHistoryQuery(
                "0007",
                Page: 3,
                PageSize: 7,
                Status: "Completed",
                CreatedFromUtc: createdFrom,
                CreatedToUtc: createdTo,
                ReadinessStatus: "Ready"));

        Assert.NotNull(history);
        Assert.Equal(3, history!.Page);
        Assert.Equal(7, history.PageSize);
        Assert.Equal(applicationId, repository.LastQuery!.ApplicationId);
        Assert.Equal("0007", repository.LastQuery.SequenceNumber);
        Assert.Equal("Completed", repository.LastQuery.Status);
        Assert.Equal(createdFrom, repository.LastQuery.CreatedFromUtc);
        Assert.Equal(createdTo, repository.LastQuery.CreatedToUtc);
        Assert.Equal("Ready", repository.LastQuery.ReadinessStatus);
        Assert.Equal(3, repository.LastQuery.Page);
        Assert.Equal(7, repository.LastQuery.PageSize);
    }

    private static SubmissionApplication Application(Guid applicationId)
        => SubmissionApplication.Rehydrate(
            applicationId,
            "APP-001",
            "US",
            "Sponsor",
            DateTime.UtcNow,
            [],
            "workspace",
            "us-fda-ectd-3.2.2");

    private static PublishJob CompletedJob(Guid applicationId, string sequenceNumber)
    {
        var job = new PublishJob(applicationId, sequenceNumber);
        job.MarkRunning();
        job.MarkCompleted("Z:/output-does-not-exist/index.xml", "Z:/package-does-not-exist.zip");
        return job;
    }

    private static PublishJobHistorySummary Summary(string readinessStatus, bool isReady)
        => new(
            ReportAvailable: true,
            ReportReadable: true,
            ReportError: null,
            ValidationProfile: "US FDA eCTD 3.2.2",
            ErrorCount: 0,
            WarningCount: 3,
            WarningSummary: "3 warnings",
            ReadinessIsReady: isReady,
            ReadinessStatus: readinessStatus,
            ReadinessBlockingErrorCount: isReady ? 0 : 1,
            ReadinessWarningCount: 3,
            ReadinessMissingMetadataFields: isReady ? [] : ["ApplicantContactName"],
            LifecycleMatchedCount: 1,
            LifecycleReplaceTargetNotFoundCount: 2,
            LifecycleDeleteTargetNotFoundCount: 0,
            LifecycleAppendTargetNotFoundCount: 0,
            LifecycleAmbiguousCount: 0,
            LifecycleCurrentSequenceCount: 0,
            ArtifactFileCount: 7,
            ArtifactTotalSizeBytes: 4096,
            ArtifactPackageSizeBytes: 2048,
            ReportPath: "Z:/report-does-not-exist.json");

    private sealed class StubApplicationRepository(SubmissionApplication application) : IApplicationRepository
    {
        public Task AddAsync(SubmissionApplication value, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(SubmissionApplication value, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(id == application.Id ? application : null);

        public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<SubmissionApplication>>([application]);
    }

    private sealed class StubPublishJobRepository(PublishJobHistoryQueryResult result) : IPublishJobRepository
    {
        public PublishJobHistoryQuery? LastQuery { get; private set; }

        public Task AddAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> UpdateHistorySummaryAsync(Guid jobId, int expectedAttemptCount, PublishJobHistorySummary summary, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(result.Items.SingleOrDefault(job => job.Id == id));

        public Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(result.Items);

        public Task<IReadOnlyCollection<PublishJob>> ListActiveAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<PublishJob>>([]);

        public Task<PublishJobHistoryQueryResult> QueryHistoryAsync(
            PublishJobHistoryQuery query,
            CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            return Task.FromResult(result);
        }
    }
}
