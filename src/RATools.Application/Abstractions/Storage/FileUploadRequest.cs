namespace RATools.Application.Abstractions.Storage;

public sealed class FileUploadRequest
{
    public required string FileName { get; init; }

    public required string MediaType { get; init; }

    public required Stream Content { get; init; }
}
