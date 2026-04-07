using RATools.Domain.Publishing;

namespace RATools.Application.Abstractions.Persistence;

public sealed record PublishJobHistoryQueryResult(
    IReadOnlyCollection<PublishJob> Items,
    int TotalCount,
    int CompletedCount,
    int FailedCount,
    int RunningCount);
