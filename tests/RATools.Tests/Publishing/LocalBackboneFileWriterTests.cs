using System.IO.Compression;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing.PackageModel;
using RATools.Infrastructure.Publishing;
using Microsoft.Extensions.Logging.Abstractions;

namespace RATools.Tests.Publishing;

public sealed class LocalBackboneFileWriterTests
{
    [Fact]
    public async Task SaveAsync_WritesGeneratedFilesDocumentsDtdsMd5AndPackageZip()
    {
        var root = CreateTempRoot();
        try
        {
            var sourceDirectory = Path.Combine(root, "source", "0001", "m1", "us", "12-cover-letters");
            Directory.CreateDirectory(sourceDirectory);
            var sourcePath = Path.Combine(sourceDirectory, "cover.pdf");
            await File.WriteAllTextAsync(sourcePath, "cover letter");

            var writer = new LocalBackboneFileWriter(Options.Create(new BackboneOutputOptions { RootPath = root }), NullLogger<LocalBackboneFileWriter>.Instance);
            var jobId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var generatedFiles = new[]
            {
                new BackboneGeneratedFile("index.xml", "<ectd:ectd />"),
                new BackboneGeneratedFile("m1/us/us-regional.xml", "<fda-regional:fda-regional />")
            };
            var publishedFiles = new[]
            {
                new EctdPublishedFile(
                    Guid.NewGuid(),
                    sourcePath,
                    "m1/us/12-cover-letters/cover.pdf",
                    "cover.pdf",
                    new FileInfo(sourcePath).Length,
                    "sha-cover",
                    "md5-cover")
            };

            var result = await writer.SaveAsync(
                "ANDA123456",
                "0001",
                jobId,
                root,
                generatedFiles,
                "publish-report-0001.json",
                "0001.zip",
                publishedFiles);

            var deliveryRoot = Path.Combine(root, "ANDA123456", "_jobs", jobId.ToString("N"), "0001");
            var indexPath = Path.Combine(deliveryRoot, "index.xml");
            var regionalPath = Path.Combine(deliveryRoot, "m1", "us", "us-regional.xml");
            var copiedDocumentPath = Path.Combine(deliveryRoot, "m1", "us", "12-cover-letters", "cover.pdf");
            var ichDtdPath = Path.Combine(deliveryRoot, "util", "dtd", "ich-ectd-3-2.dtd");
            var regionalDtdPath = Path.Combine(deliveryRoot, "util", "dtd", "us-regional-v3-3.dtd");
            var indexMd5Path = Path.Combine(deliveryRoot, "index-md5.txt");

            Assert.Equal(indexPath, result.FilePath);
            Assert.True(File.Exists(indexPath));
            Assert.True(File.Exists(regionalPath));
            Assert.True(File.Exists(copiedDocumentPath));
            Assert.True(File.Exists(ichDtdPath));
            Assert.True(File.Exists(regionalDtdPath));
            Assert.True(File.Exists(indexMd5Path));
            Assert.False(File.Exists(result.ReportPath));
            Assert.True(File.Exists(result.PackagePath));

            var md5Manifest = await File.ReadAllTextAsync(indexMd5Path);
            var expectedIndexMd5 = ComputeMd5Hex(indexPath);
            Assert.Equal($"{expectedIndexMd5}  index.xml{Environment.NewLine}", md5Manifest);
            Assert.DoesNotContain("m1/us/us-regional.xml", md5Manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("m1/us/12-cover-letters/cover.pdf", md5Manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("util/dtd/ich-ectd-3-2.dtd", md5Manifest, StringComparison.Ordinal);
            Assert.DoesNotContain("util/dtd/us-regional-v3-3.dtd", md5Manifest, StringComparison.Ordinal);

            using var archive = ZipFile.OpenRead(result.PackagePath);
            var entries = archive.Entries.Select(x => x.FullName.Replace('\\', '/')).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Contains("index.xml", entries);
            Assert.Contains("m1/us/us-regional.xml", entries);
            Assert.Contains("m1/us/12-cover-letters/cover.pdf", entries);
            Assert.Contains("util/dtd/ich-ectd-3-2.dtd", entries);
            Assert.Contains("util/dtd/us-regional-v3-3.dtd", entries);
            Assert.Contains("index-md5.txt", entries);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task SaveAsync_ThrowsWhenPublishedSourceFileIsMissing()
    {
        var root = CreateTempRoot();
        try
        {
            var writer = new LocalBackboneFileWriter(Options.Create(new BackboneOutputOptions { RootPath = root }), NullLogger<LocalBackboneFileWriter>.Instance);
            var missingSourcePath = Path.Combine(root, "source", "0001", "m3", "32-body-of-data", "quality.pdf");
            var generatedFiles = new[]
            {
                new BackboneGeneratedFile("index.xml", "<ectd:ectd />")
            };
            var publishedFiles = new[]
            {
                new EctdPublishedFile(
                    Guid.NewGuid(),
                    missingSourcePath,
                    "m3/32-body-of-data/quality.pdf",
                    "quality.pdf",
                    123,
                    "sha-quality",
                    "md5-quality")
            };

            var exception = await Assert.ThrowsAsync<FileNotFoundException>(() => writer.SaveAsync(
                "ANDA123456",
                "0001",
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                root,
                generatedFiles,
                "publish-report-0001.json",
                "0001.zip",
                publishedFiles));

            Assert.Equal(missingSourcePath, exception.FileName);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task SaveAsync_PrunesJobRunsBeyondRetention()
    {
        var root = CreateTempRoot();
        try
        {
            var applicationRoot = Path.Combine(root, "ANDA123456");
            var jobsRoot = Path.Combine(applicationRoot, "_jobs");
            var artifactsRoot = Path.Combine(applicationRoot, "_artifacts", "0001");
            Directory.CreateDirectory(artifactsRoot);
            // 预置 3 份历史 _jobs 副本（LastWriteTime 依次变旧）与 1 份交付物目录。
            var oldJobIds = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
            for (var index = 0; index < oldJobIds.Length; index += 1)
            {
                var jobDirectory = Path.Combine(jobsRoot, oldJobIds[index].ToString("N"), "0001");
                Directory.CreateDirectory(jobDirectory);
                await File.WriteAllTextAsync(Path.Combine(jobDirectory, "index.xml"), "<ectd:ectd />");
                Directory.SetLastWriteTimeUtc(
                    Path.Combine(jobsRoot, oldJobIds[index].ToString("N")),
                    DateTime.UtcNow.AddHours(-(index + 1)));
            }

            var writer = new LocalBackboneFileWriter(
                Options.Create(new BackboneOutputOptions { RootPath = root, RetainJobRuns = 2 }),
                NullLogger<LocalBackboneFileWriter>.Instance);
            var newJobId = Guid.NewGuid();

            await writer.SaveAsync(
                "ANDA123456",
                "0001",
                newJobId,
                root,
                [new BackboneGeneratedFile("index.xml", "<ectd:ectd />")],
                "publish-report-0001.json",
                "0001.zip",
                []);

            var survivingJobDirectories = Directory.GetDirectories(jobsRoot)
                .Select(Path.GetFileName)
                .ToArray();
            // 保留 2 份：本次 + 最新的一份历史；两份较旧历史被清理。
            Assert.Equal(2, survivingJobDirectories.Length);
            Assert.Contains(newJobId.ToString("N"), survivingJobDirectories);
            Assert.Contains(oldJobIds[0].ToString("N"), survivingJobDirectories);
            // 交付物目录不受保留策略影响。
            Assert.True(Directory.Exists(artifactsRoot));
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    [Fact]
    public async Task SaveAsync_KeepsAllJobRunsWhenRetentionDisabled()
    {
        var root = CreateTempRoot();
        try
        {
            var jobsRoot = Path.Combine(root, "ANDA123456", "_jobs");
            var oldJobDirectory = Path.Combine(jobsRoot, Guid.NewGuid().ToString("N"), "0001");
            Directory.CreateDirectory(oldJobDirectory);
            await File.WriteAllTextAsync(Path.Combine(oldJobDirectory, "index.xml"), "<ectd:ectd />");

            var writer = new LocalBackboneFileWriter(
                Options.Create(new BackboneOutputOptions { RootPath = root, RetainJobRuns = 0 }),
                NullLogger<LocalBackboneFileWriter>.Instance);

            await writer.SaveAsync(
                "ANDA123456",
                "0001",
                Guid.NewGuid(),
                root,
                [new BackboneGeneratedFile("index.xml", "<ectd:ectd />")],
                "publish-report-0001.json",
                "0001.zip",
                []);

            Assert.Equal(2, Directory.GetDirectories(jobsRoot).Length);
        }
        finally
        {
            DeleteIfExists(root);
        }
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"local-backbone-writer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static string ComputeMd5Hex(string path)
    {
        using var stream = File.OpenRead(path);
        using var md5 = System.Security.Cryptography.MD5.Create();
        return Convert.ToHexString(md5.ComputeHash(stream)).ToLowerInvariant();
    }

    private static void DeleteIfExists(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
