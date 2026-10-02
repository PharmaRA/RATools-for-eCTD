namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class ImportedBackboneRecord
{
    public Guid ApplicationId { get; set; }
    public string SequenceNumber { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string Xml { get; set; } = string.Empty;
}
