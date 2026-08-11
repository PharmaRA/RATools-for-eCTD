using RATools.Domain.Publishing;

namespace RATools.Application.Abstractions.Persistence;

public sealed record PublishJobHistoryQueryResult(
    IReadOnlyCollection<PublishJob> Items,
    int TotalCount,
    int CompletedCount,
    int FailedCount,
    int RunningCount,
    IReadOnlyDictionary<Guid, PublishJobHistorySummary>? HistorySummaries = null,
    PublishJobHistoryReadinessCounts? ReadinessCounts = null,
    PublishJobHistoryLifecycleCounts? LifecycleCounts = null);
