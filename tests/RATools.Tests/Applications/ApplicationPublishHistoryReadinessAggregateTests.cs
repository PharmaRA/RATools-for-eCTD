using RATools.Application.Applications;
using RATools.Application.Applications.Dtos;

namespace RATools.Tests.Applications;

public sealed class ApplicationPublishHistoryReadinessAggregateTests
{
    [Fact]
    public void Create_CountsReadinessStatusesInOneSummary()
    {
        ApplicationPublishHistoryReadinessSummaryDto?[] summaries =
        [
            Summary("Ready"),
            Summary("Blocked"),
            null,
            Summary("ready"),
        ];

        var aggregate = ApplicationPublishHistoryReadinessAggregate.Create(summaries);

        Assert.Equal(2, aggregate.ReadyCount);
        Assert.Equal(1, aggregate.BlockedCount);
        Assert.Equal(1, aggregate.UnknownCount);
    }

    private static ApplicationPublishHistoryReadinessSummaryDto Summary(string status)
        => new(
            string.Equals(status, "Ready", StringComparison.OrdinalIgnoreCase),
            status,
            0,
            0,
            []);
}
