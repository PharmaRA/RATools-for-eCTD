using RATools.Application.Applications.Dtos;

namespace RATools.Application.Applications;

internal static class ApplicationPublishHistoryStatusSummary
{
    public static ApplicationPublishHistoryStatusSummaryDto Create(IEnumerable<string> statuses)
    {
        var completedCount = 0;
        var failedCount = 0;
        var runningCount = 0;

        foreach (var status in statuses)
        {
            if (status.Equals("Completed", StringComparison.OrdinalIgnoreCase))
            {
                completedCount++;
            }
            else if (status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
            {
                failedCount++;
            }
            else if (status.Equals("Running", StringComparison.OrdinalIgnoreCase))
            {
                runningCount++;
            }
        }

        return new ApplicationPublishHistoryStatusSummaryDto(
            completedCount,
            failedCount,
            runningCount);
    }
}
