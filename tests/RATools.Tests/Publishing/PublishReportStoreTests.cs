using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Validation.Dtos;
using RATools.Domain.Publishing;

namespace RATools.Tests.Publishing;

public sealed class PublishReportStoreTests
{
    [Fact]
    public async Task ReadAsync_ThrowsWhenOutputDirectoryIsMissing()
    {
        var store = new InMemoryPublishArtifactStore();
        var reportStore = new PublishReportStore(store);
        var job = CreateCompletedJob(outputPath: Path.Combine("missing", "index.xml"));

        await Assert.ThrowsAsync<PublishJobReportUnavailableException>(() => reportStore.ReadAsync(job));
    }

    [Fact]
    public async Task ReadAsync_ThrowsWhenReportIsMissing()
    {
        var store = new InMemoryPublishArtifactStore();
        var job = CreateCompletedJob();
        store.ExistingPaths.Add(Path.GetDirectoryName(job.OutputPath!)!);
        var reportStore = new PublishReportStore(store);

        await Assert.ThrowsAsync<PublishJobReportUnavailableException>(() => reportStore.ReadAsync(job));
    }

    [Fact]
    public async Task ReadAsync_ThrowsWhenReportJsonIsCorrupted()
    {
        var store = new InMemoryPublishArtifactStore();
        var job = CreateCompletedJob();
        store.ExistingPaths.Add(Path.GetDirectoryName(job.OutputPath!)!);
        store.TextByPath[PublishOutputNaming.BuildPublishReportPath(job.OutputPath!, job.SequenceNumber, job.Id)] = "{broken";
        var reportStore = new PublishReportStore(store);

        await Assert.ThrowsAsync<PublishJobReportCorruptedException>(() => reportStore.ReadAsync(job));
    }

    [Fact]
    public async Task WriteAsync_ThenReadAsync_RoundTripsExecutionReport()
    {
        var store = new InMemoryPublishArtifactStore();
        var job = CreateCompletedJob();
        var reportPath = PublishOutputNaming.BuildPublishReportPath(job.OutputPath!, job.SequenceNumber, job.Id);
        var report = CreateReport(job, reportPath);
        var reportStore = new PublishReportStore(store);
        store.ExistingPaths.Add(Path.GetDirectoryName(job.OutputPath!)!);

        await reportStore.WriteAsync(report);
        var roundTripped = await reportStore.ReadAsync(job);

        Assert.Equal(report.ReportVersion, roundTripped.ReportVersion);
        Assert.Equal(report.ApplicationId, roundTripped.ApplicationId);
        Assert.Equal(report.PublishJob.Id, roundTripped.PublishJob.Id);
        Assert.Equal(reportPath, roundTripped.ReportPath);
    }

    [Fact]
    public async Task ReadAsync_ThrowsOperationCanceledBeforeStoreIo()
    {
        var store = new InMemoryPublishArtifactStore();
        var job = CreateCompletedJob();
        var reportStore = new PublishReportStore(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => reportStore.ReadAsync(job, cts.Token));
        Assert.Equal(0, store.CallCount);
    }

    private static PublishJob CreateCompletedJob(string? outputPath = null)
    {
        var job = PublishJob.Rehydrate(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "0001",
            PublishJobStatus.Running,
            null,
            null,
            DateTime.UtcNow,
            null,
            null);
        job.MarkCompleted(
            outputPath ?? Path.Combine("root", "ANDA123456", "_jobs", job.Id.ToString("N"), "0001", "index.xml"),
            Path.Combine("root", "ANDA123456", "_packages", "0001", "0001.zip"));
        return job;
    }

    private static PublishExecutionReportDto CreateReport(PublishJob job, string reportPath)
    {
        var jobDto = new PublishJobDto(
            job.Id,
            job.ApplicationId,
            job.SequenceNumber,
            job.Status.ToString(),
            job.OutputPath,
            job.PackagePath,
            job.CreatedUtc,
            job.CompletedUtc,
            job.FailureReason);
        var validationReport = new ValidationReportDto(
            job.ApplicationId,
            job.SequenceNumber,
            "US FDA eCTD 3.2.2",
            true,
            [],
            [],
            []);

        return new PublishExecutionReportDto(
            "1.1",
            job.ApplicationId,
            job.SequenceNumber,
            validationReport.ValidationProfile,
            reportPath,
            validationReport,
            jobDto,
            10,
            null,
            null,
            null,
            null,
            null,
            0,
            0,
            null,
            true,
            "Publish completed successfully.");
    }

    private sealed class InMemoryPublishArtifactStore : IPublishArtifactStore
    {
        public HashSet<string> ExistingPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> TextByPath { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int CallCount { get; private set; }

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            CallCount += 1;
            return Task.FromResult(ExistingPaths.Contains(path) || TextByPath.ContainsKey(path));
        }

        public Task<long> GetSizeAsync(string path, CancellationToken cancellationToken = default)
        {
            CallCount += 1;
            return Task.FromResult(TextByPath.TryGetValue(path, out var content) ? content.Length : 0L);
        }

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        {
            CallCount += 1;
            return Task.FromResult(TextByPath[path]);
        }

        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            CallCount += 1;
            TextByPath[path] = content;
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                ExistingPaths.Add(directory);
            }

            return Task.CompletedTask;
        }

        public Task<PublishArtifactDirectoryStats> GetDirectoryStatsAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            CallCount += 1;
            return Task.FromResult(new PublishArtifactDirectoryStats(0, 0));
        }
    }
}
