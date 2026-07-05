using RATools.Application.Applications;
using RATools.Application.Applications.Dtos;

namespace RATools.Tests.Applications;

public sealed class ApplicationImportIssueSummaryTests
{
    [Fact]
    public void Create_CountsSkippedAndFailedSequencesInOneSummary()
    {
        var issues = new[]
        {
            Issue("Warning", "SEQUENCE_INDEX_MISSING"),
            Issue("Warning", "SEQUENCE_INDEX_MISSING"),
            Issue("Error", "SEQUENCE_FILE_MISSING"),
            Issue("error", "SEQUENCE_INDEX_INVALID"),
            Issue("Warning", "LIFECYCLE_TARGET_MISSING"),
        };

        var summary = ApplicationImportIssueSummary.Create(issues);

        Assert.Equal(2, summary.SkippedSequenceCount);
        Assert.Equal(1, summary.FailedSequenceCount);
    }

    private static ApplicationImportIssueDto Issue(string severity, string code)
        => new(severity, code, "0001", code);
}
