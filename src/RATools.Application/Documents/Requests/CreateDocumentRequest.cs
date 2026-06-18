namespace RATools.Application.Documents.Requests;

public sealed record CreateDocumentRequest(
    string FileName,
    string MediaType,
    long FileSize,
    string Sha256,
    string Md5,
    string StoragePath);
