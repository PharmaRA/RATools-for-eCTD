using RATools.Application.Applications.Dtos;

namespace RATools.Application.Applications;

internal sealed record ApplicationImportIssueSummary(
    int SkippedSequenceCount,
    int FailedSequenceCount)
{
    public static ApplicationImportIssueSummary Create(IEnumerable<ApplicationImportIssueDto> issues)
    {
        var skippedSequenceCount = 0;
        var failedSequenceCount = 0;

        foreach (var issue in issues)
        {
            if (issue.Code == "SEQUENCE_INDEX_MISSING")
            {
                skippedSequenceCount++;
            }

            if (issue.Severity == "Error")
            {
                failedSequenceCount++;
            }
        }

        return new ApplicationImportIssueSummary(skippedSequenceCount, failedSequenceCount);
    }
}
