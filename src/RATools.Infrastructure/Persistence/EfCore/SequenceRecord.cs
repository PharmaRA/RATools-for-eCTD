namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class SequenceRecord
{
    public Guid ApplicationId { get; set; }

    public string SequenceNumber { get; set; } = string.Empty;

    public string SubmissionType { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }
}
