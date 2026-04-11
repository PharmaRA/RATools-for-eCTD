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
        var storedFileName = $"{DateTime.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}_{safeFileName}";
        var fullPath = Path.Combine(fullRootPath, storedFileName);

        await using var destination = File.Create(fullPath);
        using var sha256 = SHA256.Create();

        var buffer = new byte[81920];
        long totalBytes = 0;
        int bytesRead;

        while ((bytesRead = await request.Content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
            sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
            totalBytes += bytesRead;
        }

        sha256.TransformFinalBlock([], 0, 0);
        var hash = Convert.ToHexString(sha256.Hash!).ToLowerInvariant();

        return new FileUploadResult(
            safeFileName,
            request.MediaType.Trim(),
            totalBytes,
            hash,
            fullPath);
    }
}
