using RATools.Application.Applications.Dtos;

namespace RATools.Application.Applications;

internal static class ApplicationPublishHistoryReadinessAggregate
{
    public static ApplicationPublishHistoryReadinessAggregateDto Create(
        IEnumerable<ApplicationPublishHistoryReadinessSummaryDto?> summaries)
    {
        var readyCount = 0;
        var blockedCount = 0;
        var unknownCount = 0;

        foreach (var summary in summaries)
        {
            if (summary is null)
            {
                unknownCount++;
            }
            else if (summary.Status.Equals("Ready", StringComparison.OrdinalIgnoreCase))
            {
                readyCount++;
            }
            else if (summary.Status.Equals("Blocked", StringComparison.OrdinalIgnoreCase))
            {
                blockedCount++;
            }
        }

        return new ApplicationPublishHistoryReadinessAggregateDto(
            readyCount,
            blockedCount,
            unknownCount);
    }
}
