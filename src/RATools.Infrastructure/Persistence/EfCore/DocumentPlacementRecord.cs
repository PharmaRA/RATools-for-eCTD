namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class DocumentPlacementRecord
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public string? LeafId { get; set; }

    public Guid ApplicationId { get; set; }

    public string SequenceNumber { get; set; } = string.Empty;

    public string CtdSection { get; set; } = string.Empty;

    public string Operation { get; set; } = string.Empty;

    public string? Title { get; set; }

    public Guid? LifecycleTargetPlacementId { get; set; }

    public DateTime CreatedUtc { get; set; }
}
