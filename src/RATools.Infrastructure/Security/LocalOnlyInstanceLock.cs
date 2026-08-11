using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace RATools.Infrastructure.Security;

public sealed class LocalOnlyInstanceLock(
    IOptions<DeploymentOptions> deploymentOptions,
    IHostEnvironment environment) : IDisposable
{
    private readonly object _gate = new();
    private FileStream? _lockStream;
    private bool _disposed;

    public void Acquire()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_lockStream is not null)
            {
                return;
            }

            var configuredPath = deploymentOptions.Value.InstanceLockPath;
            if (string.IsNullOrWhiteSpace(configuredPath))
            {
                throw new InvalidOperationException("Deployment:InstanceLockPath must not be empty.");
            }

            if (Path.IsPathRooted(configuredPath))
            {
                throw new InvalidOperationException(
                    "Deployment:InstanceLockPath must be relative to the API content root.");
            }

            var contentRoot = Path.GetFullPath(environment.ContentRootPath);
            var lockPath = Path.GetFullPath(Path.Combine(contentRoot, configuredPath));
            var relativePath = Path.GetRelativePath(contentRoot, lockPath);
            if (relativePath == ".."
                || relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || Path.IsPathRooted(relativePath))
            {
                throw new InvalidOperationException(
                    "Deployment:InstanceLockPath must remain inside the API content root.");
            }

            var directory = Path.GetDirectoryName(lockPath)
                ?? throw new InvalidOperationException($"Deployment instance lock path '{lockPath}' has no parent directory.");
            Directory.CreateDirectory(directory);

            try
            {
                _lockStream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 256,
                    FileOptions.WriteThrough);
                _lockStream.SetLength(0);
                var owner = Encoding.UTF8.GetBytes(
                    $"pid={Environment.ProcessId}; machine={Environment.MachineName}; startedUtc={DateTime.UtcNow:O}{Environment.NewLine}");
                _lockStream.Write(owner);
                _lockStream.Flush(flushToDisk: true);
            }
            catch (IOException exception)
            {
                _lockStream?.Dispose();
                _lockStream = null;
                throw new InvalidOperationException(
                    $"LocalOnly deployment supports one API/worker process. Another process holds '{lockPath}'.",
                    exception);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _lockStream?.Dispose();
            _lockStream = null;
            _disposed = true;
        }
    }
}
