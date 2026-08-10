namespace RATools.Application.Abstractions.Storage;

public interface IFileStorage
{
    Task<FileUploadResult> SaveAsync(FileUploadRequest request, CancellationToken cancellationToken = default);

    Task<string> MoveAsync(string sourcePath, string destinationDirectoryPath, CancellationToken cancellationToken = default);

    Task<string> RenameAsync(string sourcePath, string targetPath, CancellationToken cancellationToken = default);
}
