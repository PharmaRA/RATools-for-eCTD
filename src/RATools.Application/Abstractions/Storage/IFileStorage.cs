namespace RATools.Application.Abstractions.Storage;

public interface IFileStorage
{
    Task<FileUploadResult> SaveAsync(FileUploadRequest request, CancellationToken cancellationToken = default);
}
