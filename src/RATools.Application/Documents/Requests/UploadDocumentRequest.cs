namespace RATools.Application.Documents.Requests;

public sealed class UploadDocumentRequest
{
    public required string FileName { get; init; }

    public required string MediaType { get; init; }

    public required Stream Content { get; init; }
}

public sealed class UploadSequenceDocumentRequest
{
    public required string FileName { get; init; }

    public required string MediaType { get; init; }

    public required string CtdSection { get; init; }

    public required Stream Content { get; init; }
}
