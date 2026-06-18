namespace RATools.Infrastructure.Persistence.EfCore;

public sealed class DocumentRecord
{
    public Guid Id { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string MediaType { get; set; } = string.Empty;

    public long FileSize { get; set; }

    public string Sha256 { get; set; } = string.Empty;

    public string Md5 { get; set; } = string.Empty;

    public string StoragePath { get; set; } = string.Empty;

    public DateTime CreatedUtc { get; set; }
}
