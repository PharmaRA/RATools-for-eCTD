using RATools.Application.Abstractions.Publishing;
using RATools.Application.Abstractions.Security;
using RecycleFileSystem = Microsoft.VisualBasic.FileIO.FileSystem;

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
        EnsureNoLinks(allowedPath);
        var directory = Path.GetDirectoryName(allowedPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporary = allowedPath + ".pending-" + Guid.NewGuid().ToString("N");
        var previous = allowedPath + ".previous-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                65536, FileOptions.Asynchronous))
            {
                await using var writer = new StreamWriter(stream, leaveOpen: true);
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            if (await File.ReadAllTextAsync(temporary, cancellationToken) != content)
                throw new IOException("The staged report does not reproduce the complete text.");
            cancellationToken.ThrowIfCancellationRequested();
            EnsureNoLinks(allowedPath);
            if (File.Exists(allowedPath)) File.Replace(temporary, allowedPath, previous);
            else File.Move(temporary, allowedPath);
        }
        finally
        {
            RecycleIfPresent(temporary);
            RecycleIfPresent(previous);
        }
    }

    private static void EnsureNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
        {
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Report paths cannot traverse a link or reparse point.");
        }
    }

    private static void RecycleIfPresent(string path)
    {
        if (!File.Exists(path)) return;
        EnsureNoLinks(path);
        // Outside the qualified Windows recycling boundary retain staging evidence.
        if (!OperatingSystem.IsWindows()) return;
        RecycleFileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
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
