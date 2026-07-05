using RATools.Application.Applications;

namespace RATools.Tests.Applications;

public sealed class ApplicationPublishHistoryStatusSummaryTests
{
    [Fact]
    public void Create_CountsPublishJobStatusesInOneSummary()
    {
        var summary = ApplicationPublishHistoryStatusSummary.Create(
            ["Completed", "completed", "Failed", "Running", "Pending"]);

        Assert.Equal(2, summary.CompletedCount);
        Assert.Equal(1, summary.FailedCount);
        Assert.Equal(1, summary.RunningCount);
    }
}
