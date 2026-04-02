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
            reportContent,
            [document],
            CancellationToken.None);

        Assert.True(File.Exists(result.FilePath));
        Assert.True(File.Exists(result.ReportPath));
        Assert.True(File.Exists(result.PackagePath));
        Assert.Equal(reportContent, await File.ReadAllTextAsync(result.ReportPath));

        using var archive = ZipFile.OpenRead(result.PackagePath);
        Assert.Contains(archive.Entries, x => x.FullName == "index.xml");
        Assert.Contains(archive.Entries, x => x.FullName == "publish-report-0000-job123.json");
        Assert.Contains(archive.Entries, x => x.FullName == "documents/source.txt");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
