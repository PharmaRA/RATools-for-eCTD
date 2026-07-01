namespace RATools.Application.Abstractions.Publishing;

public sealed record PublishArtifactDirectoryStats(int FileCount, long TotalSizeBytes);

public interface IPublishArtifactStore
{
    Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default);

    Task<long> GetSizeAsync(string path, CancellationToken cancellationToken = default);

    Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default);

    Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default);

    Task<PublishArtifactDirectoryStats> GetDirectoryStatsAsync(string directoryPath, CancellationToken cancellationToken = default);
}
