using RATools.Application.Publishing;
using RATools.Application.Validation.Dtos;

namespace RATools.Tests.Publishing;

public sealed class PublishValidationIssueReportSummaryTests
{
    [Fact]
    public void Create_CountsIssuesAndSummarizesWarningsInOneSummary()
    {
        var issues = new[]
        {
            Issue("Error", "E1", "First error"),
            Issue("error", "E2", "Second error"),
            Issue("Warning", "W1", "First warning"),
            Issue("warning", "W2", "Second warning"),
            Issue("Warning", "W3", "Third warning"),
            Issue("Warning", "W4", "Fourth warning"),
        };

        var summary = PublishValidationIssueReportSummary.Create(issues);

        Assert.Equal(2, summary.ErrorCount);
        Assert.Equal(4, summary.WarningCount);
        Assert.Equal(
            "W1: First warning | W2: Second warning | W3: Third warning | +1 more warning(s)",
            summary.WarningSummary);
    }

    [Fact]
    public void Create_ReturnsNullWarningSummary_WhenThereAreNoWarnings()
    {
        var summary = PublishValidationIssueReportSummary.Create(
            [Issue("Error", "E1", "First error")]);

        Assert.Equal(1, summary.ErrorCount);
        Assert.Equal(0, summary.WarningCount);
        Assert.Null(summary.WarningSummary);
    }

    private static ValidationIssueDto Issue(string severity, string code, string message)
        => new(severity, code, message);
}
