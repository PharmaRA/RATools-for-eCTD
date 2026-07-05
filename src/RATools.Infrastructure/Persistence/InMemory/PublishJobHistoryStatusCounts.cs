using RATools.Domain.Publishing;

namespace RATools.Infrastructure.Persistence.InMemory;

internal readonly record struct PublishJobHistoryStatusCounts(
    int Total,
    int Completed,
    int Failed,
    int Running)
{
    public static PublishJobHistoryStatusCounts Create(IEnumerable<PublishJob> jobs)
    {
        var total = 0;
        var completed = 0;
        var failed = 0;
        var running = 0;

        foreach (var job in jobs)
        {
            total++;

            switch (job.Status)
            {
                case PublishJobStatus.Completed:
                    completed++;
                    break;
                case PublishJobStatus.Failed:
                    failed++;
                    break;
                case PublishJobStatus.Running:
                    running++;
                    break;
            }
        }

        return new PublishJobHistoryStatusCounts(total, completed, failed, running);
    }
}
