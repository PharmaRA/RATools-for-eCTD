using RATools.Application.Validation.Dtos;

namespace RATools.Application.Publishing;

internal sealed record PublishValidationIssueReportSummary(
    int ErrorCount,
    int WarningCount,
    string? WarningSummary)
{
    public static PublishValidationIssueReportSummary Create(IEnumerable<ValidationIssueDto> issues)
    {
        var errorCount = 0;
        var warningCount = 0;
        var shownWarnings = new List<string>(capacity: 3);

        foreach (var issue in issues)
        {
            if (issue.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase))
            {
                errorCount++;
            }
            else if (issue.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase))
            {
                warningCount++;

                if (shownWarnings.Count < 3)
                {
                    shownWarnings.Add($"{issue.Code}: {issue.Message}");
                }
            }
        }

        return new PublishValidationIssueReportSummary(
            errorCount,
            warningCount,
            BuildWarningSummary(warningCount, shownWarnings));
    }

    private static string? BuildWarningSummary(int warningCount, List<string> shownWarnings)
    {
        if (warningCount == 0)
        {
            return null;
        }

        var summary = string.Join(" | ", shownWarnings);
        var remaining = warningCount - shownWarnings.Count;

        return remaining > 0
            ? $"{summary} | +{remaining} more warning(s)"
            : summary;
    }
}
