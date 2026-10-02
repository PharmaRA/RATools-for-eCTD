using System.Text.Json;
using RATools.Domain.Common;

namespace RATools.Application.PackageValidation;

public sealed record PackageReadLimits
{
    public int MaxEntries { get; init; } = 50_000;
    public long MaxFileBytes { get; init; } = 512L * 1024 * 1024;
    public long MaxExpandedBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public long MaxArchiveBytes { get; init; } = 4L * 1024 * 1024 * 1024;
    public long MaxCentralDirectoryBytes { get; init; } = 32L * 1024 * 1024;
    public int MaxCompressionRatio { get; init; } = 200;
    public int MaxPathLength { get; init; } = 1024;
    public int MaxPathDepth { get; init; } = 64;
    public int MaxElapsedSeconds { get; init; } = 300;

    public void Validate()
    {
        if (MaxEntries < 1 || MaxFileBytes < 1 || MaxExpandedBytes < 1 || MaxArchiveBytes < 1 || MaxCentralDirectoryBytes < 1 ||
            MaxCompressionRatio < 1 || MaxPathLength < 1 || MaxPathDepth < 1 || MaxElapsedSeconds is < 1 or > 86_400 ||
            MaxFileBytes > 9_007_199_254_740_991 || MaxExpandedBytes > 9_007_199_254_740_991 ||
            MaxArchiveBytes > 9_007_199_254_740_991 || MaxCentralDirectoryBytes > 9_007_199_254_740_991)
            throw new ArgumentException("Read limits must be explicit positive bounded values.");
    }

    public string Digest()
    {
        Validate();
        return CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
        {
            schemaVersion = 1, maxEntries = MaxEntries, maxFileBytes = MaxFileBytes, maxExpandedBytes = MaxExpandedBytes,
            maxArchiveBytes = MaxArchiveBytes, maxCentralDirectoryBytes = MaxCentralDirectoryBytes,
            maxCompressionRatio = MaxCompressionRatio, maxPathLength = MaxPathLength, maxPathDepth = MaxPathDepth,
            maxElapsedSeconds = MaxElapsedSeconds
        }));
    }
}
