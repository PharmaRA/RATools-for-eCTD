using RATools.Tests.PackageValidation;
using System.IO.Compression;
using System.Text.Json;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;
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
    public void LegacyReportMissingIndependentValidationReferenceRemainsUnvalidated()
    {
        var report = CreateReport(CreateCompletedJob(), "report.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(JsonSerializer.Serialize(report))!.AsObject();
        json.Remove(nameof(PublishExecutionReportDto.PackageValidationRunId));
        var legacy = JsonSerializer.Deserialize<PublishExecutionReportDto>(json.ToJsonString())!;
        Assert.Null(legacy.PackageValidationRunId);
        var id = Guid.NewGuid();
        var current = JsonSerializer.Deserialize<PublishExecutionReportDto>(JsonSerializer.Serialize(report with { PackageValidationRunId = id }))!;
        Assert.Equal(id, current.PackageValidationRunId);
    }

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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReadAsync_ReadsArchivedReportAfterWorkDirectoryIsPruned(bool failedLogger)
    {
        var root = Path.Combine(RetainedPackageTestFiles.ScratchParent, $"publish-report-retention-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var artifactStore = CreateLocalArtifactStore(root);
            var reportStore = new PublishReportStore(artifactStore);
            var writer = new LocalBackboneFileWriter(
                Options.Create(new BackboneOutputOptions { RootPath = root, RetainJobRuns = 1 }),
                failedLogger ? new FaultingRetentionLogger() : NullLogger<LocalBackboneFileWriter>.Instance);
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
            var retainedJob = Assert.Single(Directory.GetDirectories(Path.Combine(root, firstJob.ApplicationId.ToString("N"), "_jobs", "_recycle")));
            Assert.Equal("<ectd>first publication</ectd>", await File.ReadAllTextAsync(Path.Combine(retainedJob, "0001", "index.xml")));
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
            RetainFixture(root);
        }
    }

    [Fact]
    [Trait("Category", "PathSecurity")]
    public async Task ReadAsync_RejectsArchivedReportOutsideAllowedWorkspace()
    {
        var root = Path.Combine(RetainedPackageTestFiles.ScratchParent, $"publish-report-boundary-{Guid.NewGuid():N}");
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
            RetainFixture(root);
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

    [Fact]
    public async Task ReplacingExistingPackageRetainsItsOriginalZipBytes()
    {
        var root = Path.Combine(RetainedPackageTestFiles.ScratchParent, $"publish-report-package-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var writer = new LocalBackboneFileWriter(Options.Create(new BackboneOutputOptions { RootPath = root, RetainJobRuns = 0 }), NullLogger<LocalBackboneFileWriter>.Instance);
            var app = Guid.NewGuid();
            var first = await writer.SaveAsync(app, "0001", Guid.NewGuid(), [new("index.xml", "first bytes")], "report.json", "package.zip", []);
            var bytes = await File.ReadAllBytesAsync(first.PackagePath);
            var second = await writer.SaveAsync(app, "0001", Guid.NewGuid(), [new("index.xml", "second bytes")], "report.json", "package.zip", []);
            var retained = Assert.Single(Directory.GetFiles(Path.Combine(Path.GetDirectoryName(first.PackagePath)!, "_recycle")));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(retained));
            using var archive = ZipFile.OpenRead(second.PackagePath);
            using var reader = new StreamReader(archive.GetEntry("index.xml")!.Open());
            Assert.Equal("second bytes", await reader.ReadToEndAsync());
        }
        finally { RetainFixture(root); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RetentionFailureAndUnownedDirectoryDoNotLosePublishedArtifacts(bool failedLogger)
    {
        var root = Path.Combine(RetainedPackageTestFiles.ScratchParent, $"publish-report-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var app = Guid.NewGuid(); var firstId = Guid.NewGuid();
            var writer = new LocalBackboneFileWriter(Options.Create(new BackboneOutputOptions { RootPath = root, RetainJobRuns = 1 }),
                failedLogger ? new FaultingRetentionLogger() : NullLogger<LocalBackboneFileWriter>.Instance);
            var first = await writer.SaveAsync(app, "0001", firstId, [new("index.xml", "keep old job")], "first.json", "first.zip", []);
            var jobs = Path.Combine(root, app.ToString("N"), "_jobs");
            Directory.SetLastWriteTimeUtc(Path.Combine(jobs, firstId.ToString("N")), DateTime.UtcNow.AddHours(-1));
            var foreign = Path.Combine(jobs, "not-a-job"); Directory.CreateDirectory(foreign);
            await File.WriteAllTextAsync(Path.Combine(foreign, "keep.txt"), "other owner");
            Directory.SetLastWriteTimeUtc(foreign, DateTime.UtcNow.AddHours(-2));
            await File.WriteAllTextAsync(Path.Combine(jobs, "_recycle"), "obstruction");
            var next = await writer.SaveAsync(app, "0001", Guid.NewGuid(), [new("index.xml", "new job")], "next.json", "next.zip", []);
            Assert.Equal("keep old job", await File.ReadAllTextAsync(first.FilePath));
            Assert.Equal("other owner", await File.ReadAllTextAsync(Path.Combine(foreign, "keep.txt")));
            Assert.Equal("new job", await File.ReadAllTextAsync(next.FilePath));
            Assert.True(File.Exists(first.PackagePath)); Assert.True(File.Exists(next.PackagePath));
        }
        finally { RetainFixture(root); }
    }

    private static void RetainFixture(string root)
    {
        var full = Path.GetFullPath(root);
        var parent = Path.TrimEndingDirectorySeparator(Path.GetFullPath(RetainedPackageTestFiles.ScratchParent));
        if (!string.Equals(Path.GetDirectoryName(full), parent, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) ||
            !Path.GetFileName(full).StartsWith("publish-report-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected fixture root.");
        if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Fixture links require inspection.");
        var recycle = Path.Combine(parent, "ratools-report-tests-recycle");
        Directory.CreateDirectory(recycle);
        if ((File.GetAttributes(recycle) & FileAttributes.ReparsePoint) != 0) throw new InvalidOperationException("Recycle links require inspection.");
        var destination = Path.GetFullPath(Path.Combine(recycle, Path.GetFileName(full) + "-" + Guid.NewGuid().ToString("N")));
        if (Path.GetDirectoryName(destination) != recycle) throw new InvalidOperationException("Fixture destination escapes recycle.");
        Directory.Move(full, destination);
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public async Task RetentionEnumerationDenied_DoesNotFailAlreadyGeneratedPackage()
    {
        var root = Path.Combine(RetainedPackageTestFiles.ScratchParent, $"publish-report-enumeration-{Guid.NewGuid():N}");
        var app = Guid.NewGuid();
        var jobs = Path.Combine(root, app.ToString("N"), "_jobs");
        Directory.CreateDirectory(jobs);
        var directory = new DirectoryInfo(jobs);
        var original = directory.GetAccessControl();
        var aclBackup = Path.Combine(Environment.CurrentDirectory, "backups", "qa-07-local-20261107", "publish-retention", "acl-" + Guid.NewGuid().ToString("N") + ".bin");
        for (var ancestor = Path.GetDirectoryName(Path.GetFullPath(aclBackup)); ancestor is not null; ancestor = Path.GetDirectoryName(ancestor))
            if (Path.Exists(ancestor) && (File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("ACL backup path must not traverse links.");
        Directory.CreateDirectory(Path.GetDirectoryName(aclBackup)!);
        var originalDescriptor = original.GetSecurityDescriptorBinaryForm();
        await using (var backupStream = new FileStream(aclBackup, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await backupStream.WriteAsync(originalDescriptor);
        using var identity = WindowsIdentity.GetCurrent();
        var denied = directory.GetAccessControl();
        denied.AddAccessRule(new FileSystemAccessRule(identity.User!, FileSystemRights.ListDirectory, InheritanceFlags.None, PropagationFlags.None, AccessControlType.Deny));
        try
        {
            directory.SetAccessControl(denied);
            Assert.Throws<UnauthorizedAccessException>(() => Directory.GetDirectories(jobs));
            var writer = new LocalBackboneFileWriter(Options.Create(new BackboneOutputOptions { RootPath = root, RetainJobRuns = 1 }), NullLogger<LocalBackboneFileWriter>.Instance);
            var output = await writer.SaveAsync(app, "0001", Guid.NewGuid(), [new("index.xml", "published despite unavailable retention listing")], "report.json", "package.zip", []);
            Assert.True(File.Exists(output.PackagePath));
            Assert.Equal("published despite unavailable retention listing", await File.ReadAllTextAsync(output.FilePath));
        }
        finally
        {
            var restored = directory.GetAccessControl();
            restored.SetSecurityDescriptorBinaryForm(originalDescriptor, AccessControlSections.Access);
            directory.SetAccessControl(restored);
            Assert.Single(Directory.GetDirectories(jobs));
            RetainFixture(root);
        }
    }

    private sealed class WindowsFactAttribute : FactAttribute
    {
        public WindowsFactAttribute()
        {
            if (!OperatingSystem.IsWindows()) Skip = "Windows ACL enumeration-denial test requires Windows.";
        }
    }

    private sealed class FaultingRetentionLogger : ILogger<LocalBackboneFileWriter>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => throw new InvalidOperationException("Controlled retention logger failure");
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
