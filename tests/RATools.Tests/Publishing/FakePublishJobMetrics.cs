using System.Collections.Concurrent;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Publishing;

namespace RATools.Tests.Publishing;

public sealed class FakePublishJobMetrics : IPublishJobMetrics
{
    private readonly ConcurrentQueue<(PublishJobAttemptOutcome Outcome, TimeSpan Duration)> _attempts = new();
    private readonly ConcurrentQueue<PublishJobStatus> _terminalStatuses = new();

    public TaskCompletionSource<int> QueueDepthRecorded { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public IReadOnlyCollection<(PublishJobAttemptOutcome Outcome, TimeSpan Duration)> Attempts
        => _attempts.ToArray();

    public IReadOnlyCollection<PublishJobStatus> TerminalStatuses => _terminalStatuses.ToArray();

    public bool? QueueSampleHealthy { get; private set; }

    public void SetQueueDepth(int depth)
        => QueueDepthRecorded.TrySetResult(depth);

    public void SetQueueSampleHealth(bool successful)
        => QueueSampleHealthy = successful;

    public void RecordAttempt(PublishJobAttemptOutcome outcome, TimeSpan duration)
        => _attempts.Enqueue((outcome, duration));

    public void RecordTerminal(PublishJob job)
        => _terminalStatuses.Enqueue(job.Status);
}
