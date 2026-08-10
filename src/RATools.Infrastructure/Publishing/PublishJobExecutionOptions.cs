namespace RATools.Infrastructure.Publishing;

public sealed class PublishJobExecutionOptions
{
    public const string SectionName = "PublishJobs";

    public TimeSpan ExecutionTimeout { get; set; } = TimeSpan.FromMinutes(15);

    public int QueueCapacity { get; set; } = 32;
}
