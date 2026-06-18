namespace RATools.Application.Abstractions.Storage;

public sealed record FileUploadResult(
    string FileName,
    string MediaType,
    long FileSize,
    string Sha256,
    string Md5,
    string StoragePath);
