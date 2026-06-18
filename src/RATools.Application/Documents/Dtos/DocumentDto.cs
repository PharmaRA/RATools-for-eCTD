namespace RATools.Application.Documents.Dtos;

public sealed record DocumentDto(
    Guid Id,
    string FileName,
    string MediaType,
    long FileSize,
    string Sha256,
    string Md5,
    string StoragePath,
    DateTime CreatedUtc);
