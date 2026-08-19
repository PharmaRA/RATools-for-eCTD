using Prometheus;
using RATools.Domain.Publishing;
using RATools.Infrastructure.Publishing;

namespace RATools.Api.Monitoring;

public sealed class PrometheusPublishJobMetrics : IPublishJobMetrics
{
    private static readonly Gauge QueueDepth = Metrics.CreateGauge(
        "ratools_publish_queue_depth",
        "Number of durable publish jobs currently waiting in Pending state.");

    private static readonly Gauge QueueSampleHealth = Metrics.CreateGauge(
        "ratools_publish_queue_sample_success",
        "Whether the latest durable queue depth sample succeeded (1) or failed (0).");

    private static readonly Counter Attempts = Metrics.CreateCounter(
        "ratools_publish_job_attempts_total",
        "Publish worker attempts by outcome.",
        new CounterConfiguration { LabelNames = ["outcome"] });

    private static readonly Counter TerminalJobs = Metrics.CreateCounter(
        "ratools_publish_jobs_terminal_total",
        "Publish jobs reaching a terminal state by outcome.",
        new CounterConfiguration { LabelNames = ["outcome"] });

    private static readonly Histogram AttemptDuration = Metrics.CreateHistogram(
        "ratools_publish_job_attempt_duration_seconds",
        "Publish worker attempt duration in seconds by outcome.",
        new HistogramConfiguration
        {
            LabelNames = ["outcome"],
            Buckets = Histogram.ExponentialBuckets(0.5, 2, 12),
        });

    private static readonly Histogram JobDuration = Metrics.CreateHistogram(
        "ratools_publish_job_duration_seconds",
        "End-to-end publish job duration from creation to terminal state in seconds.",
        new HistogramConfiguration
        {
            LabelNames = ["outcome"],
            Buckets = Histogram.ExponentialBuckets(0.5, 2, 12),
        });

    public PrometheusPublishJobMetrics()
    {
        foreach (var outcome in Enum.GetValues<PublishJobAttemptOutcome>())
        {
            var label = ToLabel(outcome);
            Attempts.WithLabels(label);
            AttemptDuration.WithLabels(label);
        }

        TerminalJobs.WithLabels("completed");
        TerminalJobs.WithLabels("failed");
        JobDuration.WithLabels("completed");
        JobDuration.WithLabels("failed");
    }

    public void SetQueueDepth(int depth)
        => QueueDepth.Set(Math.Max(0, depth));

    public void SetQueueSampleHealth(bool successful)
        => QueueSampleHealth.Set(successful ? 1 : 0);

    public void RecordAttempt(PublishJobAttemptOutcome outcome, TimeSpan duration)
    {
        var label = ToLabel(outcome);
        Attempts.WithLabels(label).Inc();
        AttemptDuration.WithLabels(label).Observe(Math.Max(0, duration.TotalSeconds));
    }

    public void RecordTerminal(PublishJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        var label = job.Status switch
        {
            PublishJobStatus.Completed => "completed",
            PublishJobStatus.Failed => "failed",
            _ => throw new ArgumentOutOfRangeException(nameof(job), job.Status, "Only terminal jobs can be recorded."),
        };
        TerminalJobs.WithLabels(label).Inc();
        var completedUtc = job.CompletedUtc ?? DateTime.UtcNow;
        JobDuration.WithLabels(label).Observe(Math.Max(0, (completedUtc - job.CreatedUtc).TotalSeconds));
    }

    private static string ToLabel(PublishJobAttemptOutcome outcome)
        => outcome switch
        {
            PublishJobAttemptOutcome.Completed => "completed",
            PublishJobAttemptOutcome.Failed => "failed",
            PublishJobAttemptOutcome.Retry => "retry",
            PublishJobAttemptOutcome.LeaseLost => "lease_lost",
            PublishJobAttemptOutcome.Error => "error",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
        };
}
