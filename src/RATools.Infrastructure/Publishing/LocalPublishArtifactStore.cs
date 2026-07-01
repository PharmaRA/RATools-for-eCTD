using RATools.Application.Abstractions.Publishing;
using RATools.Application.Abstractions.Security;

namespace RATools.Infrastructure.Publishing;

public sealed class LocalPublishArtifactStore(IWorkspacePathPolicy pathPolicy) : IPublishArtifactStore
{
    public Task<bool> ExistsAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var allowedPath = pathPolicy.EnsureAllowed(path);
        return Task.FromResult(File.Exists(allowedPath) || Directory.Exists(allowedPath));
    }

    public Task<long> GetSizeAsync(string path, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var allowedPath = pathPolicy.EnsureAllowed(path);
        var size = File.Exists(allowedPath) ? new FileInfo(allowedPath).Length : 0;
        return Task.FromResult(size);
    }

    public async Task<string> ReadAllTextAsync(string path, CancellationToken cancellationToken = default)
    {
        var allowedPath = pathPolicy.EnsureAllowed(path);
        return await File.ReadAllTextAsync(allowedPath, cancellationToken);
    }

    public async Task WriteAllTextAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);

        var allowedPath = pathPolicy.EnsureAllowed(path);
        var directory = Path.GetDirectoryName(allowedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(allowedPath, content, cancellationToken);
    }

    public Task<PublishArtifactDirectoryStats> GetDirectoryStatsAsync(string directoryPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var allowedPath = pathPolicy.EnsureAllowed(directoryPath);
        if (!Directory.Exists(allowedPath))
        {
            return Task.FromResult(new PublishArtifactDirectoryStats(0, 0));
        }

        var files = Directory.GetFiles(allowedPath, "*", SearchOption.AllDirectories);
        var totalSizeBytes = files.Sum(path => new FileInfo(path).Length);
        return Task.FromResult(new PublishArtifactDirectoryStats(files.Length, totalSizeBytes));
    }
}
