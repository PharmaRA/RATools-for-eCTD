namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class PublishJobRecord
{
    public Guid Id { get; set; }

    public Guid ApplicationId { get; set; }

    public string SequenceNumber { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string? OutputPath { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime? CompletedUtc { get; set; }

    public string? FailureReason { get; set; }
}
