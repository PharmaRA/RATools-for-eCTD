using RATools.Domain.Common;

namespace RATools.Domain.Documents;

public sealed class SubmissionDocument : Entity
{
    public SubmissionDocument(string fileName, string mediaType, long fileSize, string sha256, string md5, string storagePath)
        : this(Guid.NewGuid(), fileName, mediaType, fileSize, sha256, md5, storagePath, DateTime.UtcNow, requireMd5: true)
    {
    }

    private SubmissionDocument(
        Guid id,
        string fileName,
        string mediaType,
        long fileSize,
        string sha256,
        string md5,
        string storagePath,
        DateTime createdUtc,
        bool requireMd5)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);

        // 新文档必须携带 MD5 作为 backbone 校验和的事实来源；存量行回填前可能为空，由 Rehydrate 容忍。
        if (requireMd5)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(md5);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(fileSize);

        Id = id;
        FileName = fileName.Trim();
        MediaType = mediaType.Trim();
        FileSize = fileSize;
        Sha256 = sha256.Trim();
        Md5 = (md5 ?? string.Empty).Trim();
        StoragePath = storagePath.Trim();
        CreatedUtc = createdUtc;
    }

    public string FileName { get; private set; }

    public string MediaType { get; private set; }

    public long FileSize { get; private set; }

    public string Sha256 { get; private set; }

    public string Md5 { get; private set; }

    public string StoragePath { get; private set; }

    public DateTime CreatedUtc { get; private set; }

    public static SubmissionDocument Rehydrate(
        Guid id,
        string fileName,
        string mediaType,
        long fileSize,
        string sha256,
        string md5,
        string storagePath,
        DateTime createdUtc)
    {
        return new SubmissionDocument(id, fileName, mediaType, fileSize, sha256, md5, storagePath, createdUtc, requireMd5: false);
    }

    public void Relocate(string storagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        StoragePath = storagePath.Trim();
    }

    public void ReviseFileMetadata(string fileName, string mediaType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);

        FileName = fileName.Trim();
        MediaType = mediaType.Trim();
    }
}
