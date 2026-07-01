using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing;
using RATools.Application.Publishing.Dtos;
using RATools.Domain.Publishing;

namespace RATools.Tests.Publishing;

public sealed class PublishArtifactResolverTests
{
    [Fact]
    public async Task BuildArtifactsAsync_ReturnsSupportedArtifactsWithExistenceSizeAndContentTypes()
    {
        var job = CreateCompletedJob();
        var reportPath = PublishOutputNaming.BuildPublishReportPath(job.OutputPath!, job.SequenceNumber, job.Id);
        var store = new InMemoryPublishArtifactStore();
        store.SizeByPath[job.OutputPath!] = 123;
        store.SizeByPath[reportPath] = 456;
        store.SizeByPath[job.PackagePath!] = 789;
        var resolver = new PublishArtifactResolver(store);

        var artifacts = await resolver.BuildArtifactsAsync(job);

        Assert.Equal(job.Id, artifacts.PublishJobId);
        Assert.Collection(
            artifacts.Artifacts,
            backbone =>
            {
                Assert.Equal("BackboneXml", backbone.Name);
                Assert.True(backbone.Exists);
                Assert.Equal(123, backbone.SizeBytes);
                Assert.Equal("application/xml", backbone.ContentType);
            },
            report =>
            {
                Assert.Equal("PublishReport", report.Name);
                Assert.True(report.Exists);
                Assert.Equal(456, report.SizeBytes);
                Assert.Equal("application/json", report.ContentType);
            },
            package =>
            {
                Assert.Equal("PackageZip", package.Name);
                Assert.True(package.Exists);
                Assert.Equal(789, package.SizeBytes);
                Assert.Equal("application/zip", package.ContentType);
            });
    }

    [Fact]
    public async Task ResolveAsync_IsCaseInsensitiveAndReturnsNullForUnsupportedArtifact()
    {
        var job = CreateCompletedJob();
        var store = new InMemoryPublishArtifactStore();
        store.SizeByPath[job.PackagePath!] = 789;
        var resolver = new PublishArtifactResolver(store);

        var artifact = await resolver.ResolveAsync(job, "packagezip");
        var unsupported = await resolver.ResolveAsync(job, "unknown");

        Assert.NotNull(artifact);
        Assert.Equal("PackageZip", artifact.Name);
        Assert.Null(unsupported);
    }

    [Fact]
    public async Task BuildArtifactSummaryAsync_UsesDirectoryStatsAndPackageSize()
    {
        var job = CreateCompletedJob();
        var jobDto = CreateDto(job);
        var outputDirectory = Path.GetDirectoryName(job.OutputPath!)!;
        var store = new InMemoryPublishArtifactStore();
        store.SizeByPath[job.OutputPath!] = 123;
        store.DirectoryStatsByPath[outputDirectory] = new PublishArtifactDirectoryStats(5, 1234);
        store.SizeByPath[job.PackagePath!] = 789;
        var resolver = new PublishArtifactResolver(store);

        var summary = await resolver.BuildArtifactSummaryAsync(jobDto);

        Assert.NotNull(summary);
        Assert.Equal(5, summary!.FileCount);
        Assert.Equal(1234, summary.TotalSizeBytes);
        Assert.Equal(789, summary.PackageSizeBytes);
    }

    [Fact]
    public async Task BuildArtifactsAsync_ThrowsOperationCanceledBeforeStoreIo()
    {
        var store = new InMemoryPublishArtifactStore();
        var resolver = new PublishArtifactResolver(store);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() => resolver.BuildArtifactsAsync(CreateCompletedJob(), cts.Token));
        Assert.Equal(0, store.CallCount);
    }

    private static PublishJob CreateCompletedJob()
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
            Path.Combine("root", "ANDA123456", "_jobs", job.Id.ToString("N"), "0001", "index.xml"),
            Path.Combine("root", "ANDA123456", "_packages", "0001", "0001.zip"));
        return job;
    }

    private static PublishJobDto CreateDto(PublishJob job) => new(
        job.Id,
        job.ApplicationId,
        job.SequenceNumber,
        job.Status.ToString(),
        job.OutputPath,
        job.PackagePath,
        job.CreatedUtc,
        job.CompletedUtc,
        job.FailureReason);

    private sealed class InMemoryPublishArtifactStore : IPublishArtifactStore
    {
        public Dictionary<string, long> SizeByPath { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, PublishArtifactDirectoryStats> DirectoryStatsByPath { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int CallCount { get; private set; }

        public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
        {
            CallCount += 1;
            return Task.FromResult(SizeByPath.ContainsKey(path) || DirectoryStatsByPath.ContainsKey(path));
        }

        public Task<long> GetSizeAsync(string path, CancellationToken cancellationToken = default)
        {
            CallCount += 1;
            return Task.FromResult(SizeByPath.GetValueOrDefault(path));
        }

        public Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
        {
            CallCount += 1;
            return Task.FromResult(string.Empty);
        }

        public Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
        {
            CallCount += 1;
            return Task.CompletedTask;
        }

        public Task<PublishArtifactDirectoryStats> GetDirectoryStatsAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            CallCount += 1;
            return Task.FromResult(DirectoryStatsByPath.GetValueOrDefault(directoryPath, new PublishArtifactDirectoryStats(0, 0)));
        }
    }
}
