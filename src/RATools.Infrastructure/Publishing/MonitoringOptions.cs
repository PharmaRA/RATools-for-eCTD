namespace RATools.Infrastructure.Publishing;

public sealed class MonitoringOptions
{
    public const string SectionName = "Monitoring";

    public TimeSpan QueueSampleInterval { get; set; } = TimeSpan.FromSeconds(15);
}
