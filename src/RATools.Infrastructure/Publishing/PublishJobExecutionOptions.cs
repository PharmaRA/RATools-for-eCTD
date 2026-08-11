namespace RATools.Infrastructure.Publishing;

public sealed class PublishJobExecutionOptions
{
    public const string SectionName = "PublishJobs";

    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(1);

    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);

    public int MaxAttempts { get; set; } = 3;
}
