namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class ApplicationRecord
{
    public Guid Id { get; set; }

    public string ApplicationNumber { get; set; } = string.Empty;

    public string Region { get; set; } = string.Empty;

    public string SponsorName { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }

    public List<SequenceRecord> Sequences { get; set; } = [];
}
