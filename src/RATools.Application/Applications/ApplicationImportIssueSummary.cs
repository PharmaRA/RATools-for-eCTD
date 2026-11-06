using RATools.Application.Applications.Dtos;

namespace RATools.Application.Applications;

internal sealed record ApplicationImportIssueSummary(
    int SkippedSequenceCount,
    int FailedSequenceCount)
{
    public static ApplicationImportIssueSummary Create(IEnumerable<ApplicationImportIssueDto> issues)
    {
        var skippedSequenceCount = 0;
        // Sequence identifiers use exact ordinal identity (normally four digits).
        // Severity is a label; its casing must not change sequence coverage.
        var failedSequences = new HashSet<string>(StringComparer.Ordinal);

        foreach (var issue in issues)
        {
            if (issue.Code == "SEQUENCE_INDEX_MISSING")
            {
                skippedSequenceCount++;
            }

            if (string.Equals(issue.Severity, "Error", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(issue.SequenceNumber))
            {
                failedSequences.Add(issue.SequenceNumber);
            }
        }

        return new ApplicationImportIssueSummary(skippedSequenceCount, failedSequences.Count);
    }
}
