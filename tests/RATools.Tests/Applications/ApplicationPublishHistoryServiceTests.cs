using System.Text.Json;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Validation.Dtos;
using RATools.Domain.Applications;
using RATools.Domain.Publishing;

namespace RATools.Tests.Applications;

public sealed class ApplicationPublishHistoryServiceTests
{
    [Fact]
    public async Task GetAsync_MapsPublishReadinessSummaryFromPublishReport()
    {
        using var tempRoot = new TemporaryDirectory();
        var applicationId = Guid.NewGuid();
        var publishJobId = Guid.NewGuid();
        var sequenceNumber = "0001";
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "APP-001",
            "US",
            "Sponsor",
            DateTime.UtcNow,
            [],
            tempRoot.Path,
            "us-fda-ectd-3.2.2");
        var outputPath = CreateOutputPath(tempRoot.Path, sequenceNumber, publishJobId);
        var reportPath = PublishOutputNaming.BuildPublishReportPath(outputPath, sequenceNumber, publishJobId);

        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(BuildReport(applicationId, publishJobId, sequenceNumber, outputPath)));

        var publishJob = PublishJob.Rehydrate(
            publishJobId,
            applicationId,
            sequenceNumber,
            PublishJobStatus.Completed,
            outputPath,
            Path.Combine(Path.GetDirectoryName(outputPath)!, "package.zip"),
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow,
            null);
        var service = new ApplicationPublishHistoryService(
            new StubApplicationRepository(application),
            new StubPublishJobRepository([publishJob]));

        var history = await service.GetAsync(applicationId, new ApplicationPublishHistoryQuery(null, 1, 20, null, null, null, null));

        var entry = Assert.Single(history!.Entries);
        Assert.NotNull(entry.PublishReadiness);
        Assert.False(entry.PublishReadiness!.IsReady);
        Assert.Equal("Blocked", entry.PublishReadiness.Status);
        Assert.Equal(1, entry.PublishReadiness.BlockingErrorCount);
        Assert.Equal(0, entry.PublishReadiness.WarningCount);
        Assert.Equal(["ApplicantContactName"], entry.PublishReadiness.MissingMetadataFields);
        Assert.NotNull(history.ReadinessSummary);
        Assert.Equal(0, history.ReadinessSummary!.ReadyCount);
        Assert.Equal(1, history.ReadinessSummary.BlockedCount);
        Assert.Equal(0, history.ReadinessSummary.UnknownCount);
    }

    [Theory]
    [InlineData("Blocked", false, "0001")]
    [InlineData("Ready", true, "0002")]
    public async Task GetAsync_FiltersEntriesByReadinessStatus(string readinessStatus, bool isReady, string expectedSequenceNumber)
    {
        using var tempRoot = new TemporaryDirectory();
        var applicationId = Guid.NewGuid();
        var blockedJobId = Guid.NewGuid();
        var readyJobId = Guid.NewGuid();
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "APP-001",
            "US",
            "Sponsor",
            DateTime.UtcNow,
            [],
            tempRoot.Path,
            "us-fda-ectd-3.2.2");

        var blockedJob = CreateCompletedJob(tempRoot.Path, applicationId, blockedJobId, "0001", isReady: false);
        var readyJob = CreateCompletedJob(tempRoot.Path, applicationId, readyJobId, "0002", isReady: true);
        var service = new ApplicationPublishHistoryService(
            new StubApplicationRepository(application),
            new StubPublishJobRepository([blockedJob, readyJob]));

        var history = await service.GetAsync(applicationId, new ApplicationPublishHistoryQuery(null, 1, 20, null, null, null, readinessStatus));

        var entry = Assert.Single(history!.Entries);
        Assert.Equal(expectedSequenceNumber, entry.SequenceNumber);
        Assert.NotNull(entry.PublishReadiness);
        Assert.Equal(isReady, entry.PublishReadiness!.IsReady);
        Assert.Equal(1, history.TotalCount);
    }

    [Fact]
    public async Task GetAsync_ComputesReadinessSummaryAcrossReadyBlockedAndUnknownEntries()
    {
        using var tempRoot = new TemporaryDirectory();
        var applicationId = Guid.NewGuid();
        var blockedJobId = Guid.NewGuid();
        var readyJobId = Guid.NewGuid();
        var unknownJobId = Guid.NewGuid();
        var application = SubmissionApplication.Rehydrate(
            applicationId,
            "APP-001",
            "US",
            "Sponsor",
            DateTime.UtcNow,
            [],
            tempRoot.Path,
            "us-fda-ectd-3.2.2");

        var blockedJob = CreateCompletedJob(tempRoot.Path, applicationId, blockedJobId, "0001", isReady: false);
        var readyJob = CreateCompletedJob(tempRoot.Path, applicationId, readyJobId, "0002", isReady: true);
        var unknownJob = PublishJob.Rehydrate(
            unknownJobId,
            applicationId,
            "0003",
            PublishJobStatus.Completed,
            null,
            null,
            DateTime.UtcNow.AddMinutes(-3),
            DateTime.UtcNow,
            null);
        var service = new ApplicationPublishHistoryService(
            new StubApplicationRepository(application),
            new StubPublishJobRepository([blockedJob, readyJob, unknownJob]));

        var history = await service.GetAsync(applicationId, new ApplicationPublishHistoryQuery(null, 1, 20, null, null, null, null));

        Assert.NotNull(history);
        Assert.NotNull(history!.ReadinessSummary);
        Assert.Equal(1, history.ReadinessSummary!.ReadyCount);
        Assert.Equal(1, history.ReadinessSummary.BlockedCount);
        Assert.Equal(1, history.ReadinessSummary.UnknownCount);
    }

    private static PublishJob CreateCompletedJob(string rootPath, Guid applicationId, Guid publishJobId, string sequenceNumber, bool isReady)
    {
        var outputPath = CreateOutputPath(rootPath, sequenceNumber, publishJobId);
        var reportPath = PublishOutputNaming.BuildPublishReportPath(outputPath, sequenceNumber, publishJobId);

        Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(BuildReport(applicationId, publishJobId, sequenceNumber, outputPath, isReady)));

        return PublishJob.Rehydrate(
            publishJobId,
            applicationId,
            sequenceNumber,
            PublishJobStatus.Completed,
            outputPath,
            Path.Combine(Path.GetDirectoryName(outputPath)!, "package.zip"),
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow,
            null);
    }

    private static string CreateOutputPath(string rootPath, string sequenceNumber, Guid publishJobId)
    {
        var jobDirectory = Path.Combine(rootPath, "_jobs", sequenceNumber, publishJobId.ToString("N"));
        Directory.CreateDirectory(jobDirectory);
        return Path.Combine(jobDirectory, "index.xml");
    }

    private static PublishExecutionReportDto BuildReport(Guid applicationId, Guid publishJobId, string sequenceNumber, string outputPath, bool isReady = false)
    {
        return new PublishExecutionReportDto(
            "publish-report-v1",
            applicationId,
            sequenceNumber,
            "US FDA eCTD 3.2.2",
            null,
            new ValidationReportDto(
                applicationId,
                sequenceNumber,
                "US FDA eCTD 3.2.2",
                true,
                [],
                [],
                []),
            new PublishJobDto(
                publishJobId,
                applicationId,
                sequenceNumber,
                "Completed",
                outputPath,
                Path.Combine(Path.GetDirectoryName(outputPath)!, "package.zip"),
                DateTime.UtcNow.AddMinutes(-5),
                DateTime.UtcNow,
                null),
            1500,
            null,
            null,
            new PublishReadinessReportDto(
                applicationId,
                sequenceNumber,
                isReady,
                isReady ? "Ready" : "Blocked",
                isReady ? 0 : 1,
                isReady ? 1 : 0,
                new ValidationReportDto(
                    applicationId,
                    sequenceNumber,
                    "US FDA eCTD 3.2.2",
                    true,
                    [],
                    [],
                    []),
                isReady ? [] : ["ApplicantContactName"],
                [
                    new PublishReadinessCategorySummaryDto(
                        isReady ? "Validation" : "RegionalMetadata",
                        isReady ? 0 : 1,
                        isReady ? 1 : 0,
                        1),
                ],
                [
                    new PublishReadinessFindingDto(
                        isReady ? "Validation" : "PublishPreflight",
                        isReady ? "Warning" : "Error",
                        isReady ? "TITLE_FALLBACK_USED" : "US_REGIONAL_METADATA_MISSING",
                        isReady ? "Placement has no explicit title, so the file name will be used." : "metadata field 'ApplicantContactName' is required.",
                        isReady ? "Validation" : "RegionalMetadata",
                        isReady ? "Resolve the validation issue before publishing." : "Populate the required US Regional publishing metadata field before publishing.",
                        isReady ? null : "ApplicantContactName"),
                ]),
            new PublishArtifactSummaryDto(7, 4096, 2048),
            null,
            0,
            0,
            null,
            true,
            "Publish completed successfully.");
    }

    private sealed class StubApplicationRepository(SubmissionApplication application) : IApplicationRepository
    {
        public Task AddAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(SubmissionApplication application, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<SubmissionApplication?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(id == application.Id ? application : null);

        public Task<IReadOnlyCollection<SubmissionApplication>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<SubmissionApplication>>([application]);
    }

    private sealed class StubPublishJobRepository(IReadOnlyCollection<PublishJob> publishJobs) : IPublishJobRepository
    {
        public Task AddAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(publishJobs.SingleOrDefault(x => x.Id == id));

        public Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(publishJobs);

        public Task<PublishJobHistoryQueryResult> QueryHistoryAsync(PublishJobHistoryQuery query, CancellationToken cancellationToken = default)
        {
            var filtered = publishJobs
                .Where(x => x.ApplicationId == query.ApplicationId)
                .ToArray();

            return Task.FromResult(new PublishJobHistoryQueryResult(
                filtered,
                filtered.Length,
                filtered.Count(x => x.Status == PublishJobStatus.Completed),
                filtered.Count(x => x.Status == PublishJobStatus.Failed),
                filtered.Count(x => x.Status == PublishJobStatus.Running)));
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ratools-publish-history-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
