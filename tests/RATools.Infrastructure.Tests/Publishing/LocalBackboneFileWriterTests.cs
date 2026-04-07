using System.IO.Compression;
using Microsoft.Extensions.Options;
using RATools.Domain.Documents;
using RATools.Infrastructure.Publishing;
using Xunit;

namespace RATools.Infrastructure.Tests.Publishing;

public sealed class LocalBackboneFileWriterTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), "ratools-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task SaveAsync_WritesBackboneReportAndPackageContents()
    {
        Directory.CreateDirectory(_tempRoot);
        var sourceFile = Path.Combine(_tempRoot, "source.txt");
        await File.WriteAllTextAsync(sourceFile, "sample file");

        var options = Options.Create(new BackboneOutputOptions
        {
            RootPath = Path.Combine(_tempRoot, "publish")
        });

        var writer = new LocalBackboneFileWriter(options);
        var applicationId = Guid.NewGuid();
        var document = SubmissionDocument.Rehydrate(
            Guid.NewGuid(),
            "source.txt",
            "text/plain",
            11,
            "abc123",
            sourceFile,
            DateTime.UtcNow);

        var reportContent = "{\"reportVersion\":\"1.1\"}";

        var result = await writer.SaveAsync(
            applicationId,
            "0000",
            "index.xml",
            "<ectd />",
            "publish-report-0000-job123.json",
            "0000-job123.zip",
            reportContent,
            [document],
            CancellationToken.None);

        Assert.True(File.Exists(result.FilePath));
        Assert.True(File.Exists(result.ReportPath));
        Assert.True(File.Exists(result.PackagePath));
        Assert.EndsWith("0000-job123.zip", result.PackagePath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(reportContent, await File.ReadAllTextAsync(result.ReportPath));

        using var archive = ZipFile.OpenRead(result.PackagePath);
        Assert.Contains(archive.Entries, x => x.FullName == "index.xml");
        Assert.Contains(archive.Entries, x => x.FullName == "publish-report-0000-job123.json");
        Assert.Contains(archive.Entries, x => x.FullName == $"documents/{document.Id:N}_source.txt");
    }

    [Fact]
    public async Task SaveAsync_RenamesDocumentsWhenSourceFileNamesCollide()
    {
        Directory.CreateDirectory(_tempRoot);
        var sourceFile1 = Path.Combine(_tempRoot, "source-1.txt");
        var sourceFile2 = Path.Combine(_tempRoot, "source-2.txt");
        await File.WriteAllTextAsync(sourceFile1, "one");
        await File.WriteAllTextAsync(sourceFile2, "two");

        var options = Options.Create(new BackboneOutputOptions { RootPath = Path.Combine(_tempRoot, "publish") });
        var writer = new LocalBackboneFileWriter(options);
        var applicationId = Guid.NewGuid();

        var document1 = SubmissionDocument.Rehydrate(Guid.Parse("00000000-0000-0000-0000-000000000011"), "same.txt", "text/plain", 3, "hash1", sourceFile1, DateTime.UtcNow);
        var document2 = SubmissionDocument.Rehydrate(Guid.Parse("00000000-0000-0000-0000-000000000022"), "same.txt", "text/plain", 3, "hash2", sourceFile2, DateTime.UtcNow);

        var result = await writer.SaveAsync(
            applicationId,
            "0000",
            "index.xml",
            "<ectd />",
            "publish-report.json",
            "0000.zip",
            "{}",
            [document1, document2],
            CancellationToken.None);

        using var archive = ZipFile.OpenRead(result.PackagePath);
        Assert.Contains(archive.Entries, x => x.FullName == "documents/00000000000000000000000000000011_same.txt");
        Assert.Contains(archive.Entries, x => x.FullName == "documents/00000000000000000000000000000022_same.txt");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
