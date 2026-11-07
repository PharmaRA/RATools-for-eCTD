using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Storage;

namespace RATools.Infrastructure.Storage;

public sealed class LocalFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    public async Task<FileUploadResult> SaveAsync(FileUploadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FileName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MediaType);
        ArgumentNullException.ThrowIfNull(request.Content);

        var rootPath = string.IsNullOrWhiteSpace(request.DestinationDirectoryPath)
            ? options.Value.RootPath
            : request.DestinationDirectoryPath;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            throw new InvalidOperationException("File storage root path is not configured.");
        }

        var fullRootPath = Path.GetFullPath(rootPath);
        Directory.CreateDirectory(fullRootPath);

        var safeFileName = Path.GetFileName(request.FileName.Trim());
        var storedFileName = request.PreserveFileName ? safeFileName : $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}_{safeFileName}";
        var fullPath = Path.Combine(fullRootPath, storedFileName);

        var created = false;
        try
        {
            await using var destination = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            using var sha256 = SHA256.Create();
            using var md5 = MD5.Create();

            var buffer = new byte[81920];
            long totalBytes = 0;
            int bytesRead;

            while ((bytesRead = await request.Content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
            {
                await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                md5.TransformBlock(buffer, 0, bytesRead, null, 0);
                totalBytes += bytesRead;
            }

            sha256.TransformFinalBlock([], 0, 0);
            md5.TransformFinalBlock([], 0, 0);
            var hash = Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
            var md5Hash = Convert.ToHexString(md5.Hash!).ToLowerInvariant();

            return new FileUploadResult(
                safeFileName,
                request.MediaType.Trim(),
                totalBytes,
                hash,
                md5Hash,
                fullPath);
        }
        catch (Exception uploadException)
        {
            // The destination has been disposed. Retain partial bytes without
            // replacing the original failure or touching a pre-existing file.
            if (created)
            {
                try { RecycleFailedUpload(fullPath, fullRootPath); }
                catch (Exception retentionException) when (retentionException is IOException or UnauthorizedAccessException or InvalidOperationException)
                {
                    uploadException.Data["UploadRetentionFailure"] = retentionException;
                }
            }
            throw;
        }
    }

    private static void RecycleFailedUpload(string path, string root)
    {
        path = Path.GetFullPath(path);
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!string.Equals(Path.GetDirectoryName(path), root, comparison))
            throw new InvalidOperationException("Failed upload escaped its owning directory.");
        EnsureNoLinks(path);
        if (!File.Exists(path)) return;
        var recycle = Path.Combine(root, "_recycle");
        EnsureNoLinks(recycle); Directory.CreateDirectory(recycle); EnsureNoLinks(recycle);
        var destination = Path.GetFullPath(Path.Combine(recycle, Guid.NewGuid().ToString("N")));
        if (!string.Equals(Path.GetDirectoryName(destination), recycle, comparison))
            throw new InvalidOperationException("Failed upload recycle destination escaped.");
        EnsureNoLinks(path);
        File.Move(path, destination);
    }

    private static void EnsureNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if (Path.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Upload retention paths must not traverse links.");
    }

    public Task<string> MoveAsync(string sourcePath, string destinationDirectoryPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectoryPath);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"Source file '{fullSourcePath}' was not found.", fullSourcePath);
        }

        var fullDestinationDirectoryPath = Path.GetFullPath(destinationDirectoryPath);
        Directory.CreateDirectory(fullDestinationDirectoryPath);

        var targetPath = Path.Combine(fullDestinationDirectoryPath, Path.GetFileName(fullSourcePath));
        if (string.Equals(fullSourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(targetPath);
        }

        if (File.Exists(targetPath))
        {
            throw new IOException($"Destination file '{targetPath}' already exists.");
        }

        File.Move(fullSourcePath, targetPath);
        return Task.FromResult(targetPath);
    }

    public Task<string> RenameAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        var fullSourcePath = Path.GetFullPath(sourcePath);
        var fullTargetPath = Path.GetFullPath(targetPath);
        if (string.Equals(fullSourcePath, fullTargetPath, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(fullTargetPath);
        }

        if (!File.Exists(fullSourcePath))
        {
            throw new FileNotFoundException($"Source file '{fullSourcePath}' was not found.", fullSourcePath);
        }

        if (File.Exists(fullTargetPath))
        {
            throw new IOException($"Destination file '{fullTargetPath}' already exists.");
        }

        var targetDirectory = Path.GetDirectoryName(fullTargetPath)
            ?? throw new InvalidOperationException($"Target path '{fullTargetPath}' does not have a parent directory.");
        Directory.CreateDirectory(targetDirectory);
        File.Move(fullSourcePath, fullTargetPath);
        return Task.FromResult(fullTargetPath);
    }
}
