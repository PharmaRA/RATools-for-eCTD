using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Application.Validation.Dtos;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Publishing;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;

namespace RATools.Tests.Publishing;

public sealed class PublishReportStoreTests
{
    [Fact]
    public async Task ReadAsync_ThrowsWhenReportAndWorkDirectoryAreMissing()
    {
        var store = new InMemoryPublishArtifactStore();
        var reportStore = new PublishReportStore(store);
        var job = CreateCompletedJob(outputPath: Path.Combine("missing", "index.xml"));

        var exception = await Assert.ThrowsAsync<PublishJobReportUnavailableException>(() => reportStore.ReadAsync(job));

        Assert.Contains("Publish report", exception.Message);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WriteAsync_ThenReadAsync_RoundTripsReportWithoutWorkDirectory(bool legacyLayout)
    {
        var store = new InMemoryPublishArtifactStore();
        var job = CreateCompletedJob(legacyLayout: legacyLayout);
        var reportPath = PublishOutputNaming.BuildPublishReportPath(job.OutputPath!, job.SequenceNumber, job.Id);
        var report = CreateReport(job, reportPath);
        var reportStore = new PublishReportStore(store);

        await reportStore.WriteAsync(report);
        Assert.False(await store.ExistsAsync(Path.GetDirectoryName(job.OutputPath!)!));
        var roundTripped = await reportStore.ReadAsync(job);

        Assert.Equal(report.ReportVersion, roundTripped.ReportVersion);
        Assert.Equal(report.ApplicationId, roundTripped.ApplicationId);
        Assert.Equal(report.PublishJob.Id, roundTripped.PublishJob.Id);
        Assert.Equal(reportPath, roundTripped.ReportPath);
    }

    [Fact]
    public async Task ReadAsync_ReadsArchivedReportAfterWorkDirectoryIsPruned()
    {
        var root = Path.Combine(Path.GetTempPath(), $"publish-report-retention-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var artifactStore = CreateLocalArtifactStore(root);
            var reportStore = new PublishReportStore(artifactStore);
            var writer = new LocalBackboneFileWriter(
                Options.Create(new BackboneOutputOptions { RootPath = root, RetainJobRuns = 1 }),
                NullLogger<LocalBackboneFileWriter>.Instance);
            var firstJob = new PublishJob(Guid.NewGuid(), "0001");
            firstJob.MarkRunning();
            var firstOutput = await writer.SaveAsync(
                firstJob.ApplicationId, firstJob.SequenceNumber, firstJob.Id,
                [new BackboneGeneratedFile("index.xml", "<ectd>first publication</ectd>")],
                $"publish-report-0001-{firstJob.Id:N}.json", $"0001-{firstJob.Id:N}.zip", []);
            firstJob.MarkCompleted(firstOutput.FilePath, firstOutput.PackagePath);
            var firstReport = CreateReport(firstJob, firstOutput.ReportPath);
            await reportStore.WriteAsync(firstReport);

            var firstJobDirectory = Path.Combine(root, firstJob.ApplicationId.ToString("N"), "_jobs", firstJob.Id.ToString("N"));
            Assert.True(Directory.Exists(firstJobDirectory));
            Directory.SetLastWriteTimeUtc(firstJobDirectory, DateTime.UtcNow.AddHours(-1));

            var secondJob = new PublishJob(firstJob.ApplicationId, firstJob.SequenceNumber);
            secondJob.MarkRunning();
            var secondOutput = await writer.SaveAsync(
                secondJob.ApplicationId, secondJob.SequenceNumber, secondJob.Id,
                [new BackboneGeneratedFile("index.xml", "<ectd>second publication</ectd>")],
                $"publish-report-0001-{secondJob.Id:N}.json", $"0001-{secondJob.Id:N}.zip", []);
            secondJob.MarkCompleted(secondOutput.FilePath, secondOutput.PackagePath);
            await reportStore.WriteAsync(CreateReport(secondJob, secondOutput.ReportPath));

            Assert.False(Directory.Exists(firstJobDirectory));
            Assert.True(File.Exists(firstOutput.ReportPath));
            Assert.True(File.Exists(firstOutput.PackagePath));
            Assert.True(File.Exists(secondOutput.FilePath));
            var restoredReport = await reportStore.ReadAsync(firstJob);
            Assert.Equal(firstJob.Id, restoredReport.PublishJob.Id);
            Assert.Equal(firstReport.ReportPath, restoredReport.ReportPath);
            Assert.Equal(firstReport.ValidationReport.ValidationProfile, restoredReport.ValidationReport.ValidationProfile);
            Assert.Equal(firstOutput.PackagePath, restoredReport.PublishJob.PackagePath);

            var artifacts = await new PublishArtifactResolver(artifactStore).BuildArtifactsAsync(firstJob);
            Assert.False(Assert.Single(artifacts.Artifacts, artifact => artifact.Name == "BackboneXml").Exists);
            Assert.True(Assert.Single(artifacts.Artifacts, artifact => artifact.Name == "PublishReport").Exists);
            Assert.True(Assert.Single(artifacts.Artifacts, artifact => artifact.Name == "PackageZip").Exists);
            using var archive = ZipFile.OpenRead(firstOutput.PackagePath);
            using var reader = new StreamReader(archive.GetEntry("index.xml")!.Open());
            Assert.Equal("<ectd>first publication</ectd>", await reader.ReadToEndAsync());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    [Trait("Category", "PathSecurity")]
    public async Task ReadAsync_RejectsArchivedReportOutsideAllowedWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"publish-report-boundary-{Guid.NewGuid():N}");
        var allowedRoot = Path.Combine(root, "allowed");
        Directory.CreateDirectory(allowedRoot);
        try
        {
            var job = CreateCompletedJob(Path.Combine(root, "outside", "application", "0001", "index.xml"));
            var reportPath = PublishOutputNaming.BuildPublishReportPath(job.OutputPath!, job.SequenceNumber, job.Id);
            var json = JsonSerializer.Serialize(CreateReport(job, reportPath));
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
            await File.WriteAllTextAsync(reportPath, json);
            Assert.False(Directory.Exists(Path.GetDirectoryName(job.OutputPath!)));
            var reportStore = new PublishReportStore(CreateLocalArtifactStore(allowedRoot));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => reportStore.ReadAsync(job));

            Assert.Contains("outside the configured workspace roots", exception.Message);
            Assert.Equal(json, await File.ReadAllTextAsync(reportPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
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

    private static LocalPublishArtifactStore CreateLocalArtifactStore(string allowedRoot)
        => new(new ConfiguredWorkspacePathPolicy(Options.Create(new SecurityOptions { AllowedWorkspaceRoots = [allowedRoot] })));

    private static PublishJob CreateCompletedJob(string? outputPath = null, bool legacyLayout = false)
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
            outputPath ?? (legacyLayout
                ? Path.Combine("root", "ANDA123456", "0001", "index.xml")
                : Path.Combine("root", "ANDA123456", "_jobs", job.Id.ToString("N"), "0001", "index.xml")),
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
