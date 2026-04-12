using RATools.Domain.Common;

namespace RATools.Domain.Documents;

public sealed class SubmissionDocument : Entity
{
    public SubmissionDocument(string fileName, string mediaType, long fileSize, string sha256, string storagePath)
        : this(Guid.NewGuid(), fileName, mediaType, fileSize, sha256, storagePath, DateTime.UtcNow)
    {
    }

    private SubmissionDocument(
        Guid id,
        string fileName,
        string mediaType,
        long fileSize,
        string sha256,
        string storagePath,
        DateTime createdUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);

        if (fileSize < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fileSize));
        }

        Id = id;
        FileName = fileName.Trim();
        MediaType = mediaType.Trim();
        FileSize = fileSize;
        Sha256 = sha256.Trim();
        StoragePath = storagePath.Trim();
        CreatedUtc = createdUtc;
    }

    public string FileName { get; private set; }

    public string MediaType { get; private set; }

    public long FileSize { get; private set; }

    public string Sha256 { get; private set; }

    public string StoragePath { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public static SubmissionDocument Rehydrate(
        Guid id,
        string fileName,
        string mediaType,
        long fileSize,
        string sha256,
        string storagePath,
        DateTime createdUtc)
    {
        return new SubmissionDocument(id, fileName, mediaType, fileSize, sha256, storagePath, createdUtc);
    }

    public void Relocate(string storagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        StoragePath = storagePath.Trim();
    }
}
