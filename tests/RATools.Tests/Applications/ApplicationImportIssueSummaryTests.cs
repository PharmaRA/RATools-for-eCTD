using RATools.Application.Applications;
using RATools.Application.Applications.Dtos;

namespace RATools.Tests.Applications;

public sealed class ApplicationImportIssueSummaryTests
{
    [Fact]
    public void MultipleErrorsOnOneSequenceCountOnceAndSeverityIsCaseInsensitive()
    {
        ApplicationImportIssueDto[] issues =
        [
            new("Error", "SEQUENCE_FILE_MISSING", "0001", "First error"),
            new("Error", "SEQUENCE_INDEX_INVALID", "0001", "Second error"),
            new("Error", "SEQUENCE_CHECKSUM_MISMATCH", "0001", "Third error"),
            new("error", "SEQUENCE_CHECKSUM_MISMATCH", "0002", "Lowercase severity"),
            new("ERROR", "SEQUENCE_INDEX_INVALID", "0002", "Uppercase severity"),
            new("Warning", "LIFECYCLE_TARGET_MISSING", "0003", "Warning")
        ];

        Assert.Equal(2, ApplicationImportIssueSummary.Create(issues).FailedSequenceCount);
    }

    [Theory]
    [InlineData("Error")]
    [InlineData("error")]
    [InlineData("ERROR")]
    public void ErrorSeverityCaseDoesNotChangeTheFailedSequenceCount(string severity)
    {
        Assert.Equal(1, ApplicationImportIssueSummary.Create([Issue(severity, "SEQUENCE_FILE_MISSING")]).FailedSequenceCount);
    }

    [Fact]
    public void ApplicationLevelErrorsDoNotInventFailedSequences()
    {
        ApplicationImportIssueDto[] issues =
        [
            new("Error", "IMPORT_FAILED", null, "Application error"),
            new("Error", "IMPORT_FAILED", "", "No sequence"),
            new("Error", "IMPORT_FAILED", " ", "No sequence"),
            new("Error", "SEQUENCE_FILE_MISSING", "0001", "Actual sequence")
        ];

        Assert.Equal(1, ApplicationImportIssueSummary.Create(issues).FailedSequenceCount);
    }

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
