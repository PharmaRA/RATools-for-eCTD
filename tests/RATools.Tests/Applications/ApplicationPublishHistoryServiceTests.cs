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
            new StubPublishJobRepository(publishJob));

        var history = await service.GetAsync(applicationId, new ApplicationPublishHistoryQuery(null, 1, 20, null, null, null));

        var entry = Assert.Single(history!.Entries);
        Assert.NotNull(entry.PublishReadiness);
        Assert.False(entry.PublishReadiness!.IsReady);
        Assert.Equal("Blocked", entry.PublishReadiness.Status);
        Assert.Equal(1, entry.PublishReadiness.BlockingErrorCount);
        Assert.Equal(0, entry.PublishReadiness.WarningCount);
        Assert.Equal(["ApplicantContactName"], entry.PublishReadiness.MissingMetadataFields);
    }

    private static string CreateOutputPath(string rootPath, string sequenceNumber, Guid publishJobId)
    {
        var jobDirectory = Path.Combine(rootPath, "_jobs", sequenceNumber, publishJobId.ToString("N"));
        Directory.CreateDirectory(jobDirectory);
        return Path.Combine(jobDirectory, "index.xml");
    }

    private static PublishExecutionReportDto BuildReport(Guid applicationId, Guid publishJobId, string sequenceNumber, string outputPath)
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
                false,
                "Blocked",
                1,
                0,
                new ValidationReportDto(
                    applicationId,
                    sequenceNumber,
                    "US FDA eCTD 3.2.2",
                    true,
                    [],
                    [],
                    []),
                ["ApplicantContactName"],
                [
                    new PublishReadinessCategorySummaryDto(
                        "RegionalMetadata",
                        1,
                        0,
                        1),
                ],
                [
                    new PublishReadinessFindingDto(
                        "PublishPreflight",
                        "Error",
                        "US_REGIONAL_METADATA_MISSING",
                        "metadata field 'ApplicantContactName' is required.",
                        "RegionalMetadata",
                        "Populate the required US Regional publishing metadata field before publishing.",
                        "ApplicantContactName"),
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

    private sealed class StubPublishJobRepository(PublishJob publishJob) : IPublishJobRepository
    {
        public Task AddAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(PublishJob job, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<PublishJob?> GetAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(id == publishJob.Id ? publishJob : null);

        public Task<IReadOnlyCollection<PublishJob>> ListAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyCollection<PublishJob>>([publishJob]);

        public Task<PublishJobHistoryQueryResult> QueryHistoryAsync(PublishJobHistoryQuery query, CancellationToken cancellationToken = default)
            => Task.FromResult(new PublishJobHistoryQueryResult([publishJob], 1, 1, 0, 0));
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
