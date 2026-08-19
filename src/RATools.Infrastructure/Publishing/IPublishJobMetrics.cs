using RATools.Domain.Publishing;

namespace RATools.Infrastructure.Publishing;

public enum PublishJobAttemptOutcome
{
    Completed,
    Failed,
    Retry,
    LeaseLost,
    Error,
}

public interface IPublishJobMetrics
{
    void SetQueueDepth(int depth);

    void SetQueueSampleHealth(bool successful);

    void RecordAttempt(PublishJobAttemptOutcome outcome, TimeSpan duration);

    void RecordTerminal(PublishJob job);
}
