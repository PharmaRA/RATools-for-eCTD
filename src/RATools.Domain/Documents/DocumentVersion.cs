using System.Security.Cryptography;
using RATools.Domain.Common;

namespace RATools.Domain.Documents;

/// <summary>
/// Immutable bytes and their provenance. A version is never relocated or renamed after
/// creation; a new workspace document is created when a processed version is adopted.
/// </summary>
public sealed class DocumentVersion : Entity
{
    private DocumentVersion(
        Guid id,
        Guid applicationId,
        string sequenceNumber,
        Guid sourceDocumentId,
        Guid? parentVersionId,
        string fileName,
        string mediaType,
        long fileSize,
        string sha256,
        string md5,
        string storagePath,
        DateTime createdUtc,
        string engineVersion,
        string parametersJson,
        string planDigest)
    {
        if (id == Guid.Empty) throw new ArgumentException("A document version identity is required.", nameof(id));
        if (applicationId == Guid.Empty) throw new ArgumentException("An application identity is required.", nameof(applicationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(sequenceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(sha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(md5);
        ArgumentException.ThrowIfNullOrWhiteSpace(storagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(engineVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(parametersJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(planDigest);
        ArgumentOutOfRangeException.ThrowIfNegative(fileSize);

        Id = id;
        ApplicationId = applicationId;
        SequenceNumber = sequenceNumber.Trim();
        SourceDocumentId = sourceDocumentId;
        ParentVersionId = parentVersionId;
        FileName = fileName.Trim();
        MediaType = mediaType.Trim();
        FileSize = fileSize;
        Sha256 = NormalizeDigest(sha256, 64, nameof(sha256));
        Md5 = NormalizeDigest(md5, 32, nameof(md5));
        StoragePath = Path.GetFullPath(storagePath.Trim());
        CreatedUtc = createdUtc.Kind == DateTimeKind.Utc ? createdUtc : createdUtc.ToUniversalTime();
        EngineVersion = engineVersion.Trim();
        ParametersJson = parametersJson;
        PlanDigest = NormalizeDigest(planDigest, 64, nameof(planDigest));
    }

    public Guid ApplicationId { get; }
    public string SequenceNumber { get; }
    public Guid SourceDocumentId { get; }
    public Guid? ParentVersionId { get; }
    public string FileName { get; }
    public string MediaType { get; }
    public long FileSize { get; }
    public string Sha256 { get; }
    public string Md5 { get; }
    public string StoragePath { get; }
    public DateTime CreatedUtc { get; }
    public string EngineVersion { get; }
    public string ParametersJson { get; }
    public string PlanDigest { get; }

    public static DocumentVersion CreateSource(
        Guid sourceDocumentId,
        Guid applicationId,
        string sequenceNumber,
        string fileName,
        string mediaType,
        long fileSize,
        string sha256,
        string md5,
        string storagePath,
        DateTime createdUtc)
    {
        if (sourceDocumentId == Guid.Empty) throw new ArgumentException("A source document identity is required.", nameof(sourceDocumentId));
        return new(Guid.NewGuid(), applicationId, sequenceNumber, sourceDocumentId, null, fileName, mediaType,
            fileSize, sha256, md5, storagePath, createdUtc, "source-document-v1", "{}", Digest("source-document-v1", sha256, md5));
    }

    public static DocumentVersion CreateResult(
        Guid applicationId,
        string sequenceNumber,
        Guid sourceDocumentId,
        Guid parentVersionId,
        string fileName,
        string mediaType,
        long fileSize,
        string sha256,
        string md5,
        string storagePath,
        DateTime createdUtc,
        string engineVersion,
        string parametersJson,
        string planDigest)
    {
        if (sourceDocumentId == Guid.Empty) throw new ArgumentException("A source document identity is required.", nameof(sourceDocumentId));
        if (parentVersionId == Guid.Empty) throw new ArgumentException("A parent version identity is required.", nameof(parentVersionId));
        return new(Guid.NewGuid(), applicationId, sequenceNumber, sourceDocumentId, parentVersionId, fileName, mediaType,
            fileSize, sha256, md5, storagePath, createdUtc, engineVersion, parametersJson, planDigest);
    }

    public static DocumentVersion Rehydrate(
        Guid id,
        Guid applicationId,
        string sequenceNumber,
        Guid sourceDocumentId,
        Guid? parentVersionId,
        string fileName,
        string mediaType,
        long fileSize,
        string sha256,
        string md5,
        string storagePath,
        DateTime createdUtc,
        string engineVersion,
        string parametersJson,
        string planDigest)
        => new(id, applicationId, sequenceNumber, sourceDocumentId, parentVersionId, fileName, mediaType, fileSize,
            sha256, md5, storagePath, createdUtc, engineVersion, parametersJson, planDigest);

    private static string NormalizeDigest(string value, int length, string parameterName)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized.Length != length || normalized.Any(character => !Uri.IsHexDigit(character)))
            throw new ArgumentException($"{parameterName} must be a {length}-character hexadecimal digest.", parameterName);
        return normalized;
    }

    private static string Digest(string engineVersion, string sha256, string md5)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes($"{engineVersion}\n{sha256.Trim().ToLowerInvariant()}\n{md5.Trim().ToLowerInvariant()}");
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
