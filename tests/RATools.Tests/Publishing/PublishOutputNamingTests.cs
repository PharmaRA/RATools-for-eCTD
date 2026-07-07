using RATools.Application.Publishing;

namespace RATools.Tests.Publishing;

public sealed class PublishOutputNamingTests
{
    [Fact]
    public void BuildPublishReportPath_UsesJobScopedReportDirectoryForNewLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), $"publish-naming-{Guid.NewGuid():N}");
        var jobId = Guid.NewGuid();
        var outputPath = Path.Combine(root, "APP-001", "_jobs", jobId.ToString("N"), "0001", "index.xml");

        var reportPath = PublishOutputNaming.BuildPublishReportPath(outputPath, "0001", jobId);

        Assert.Equal(
            Path.Combine(root, "APP-001", "_artifacts", "0001", jobId.ToString("N"), $"publish-report-0001-{jobId:N}.json"),
            reportPath);
    }

    [Fact]
    public void BuildPublishReportPath_UsesLegacyReportDirectoryForLegacyLayout()
    {
        var root = Path.Combine(Path.GetTempPath(), $"publish-naming-{Guid.NewGuid():N}");
        var jobId = Guid.NewGuid();
        var outputPath = Path.Combine(root, "APP-001", "0001", "index.xml");

        var reportPath = PublishOutputNaming.BuildPublishReportPath(outputPath, "0001", jobId);

        Assert.Equal(
            Path.Combine(root, "APP-001", "_artifacts", "0001", $"publish-report-0001-{jobId:N}.json"),
            reportPath);
    }
}
