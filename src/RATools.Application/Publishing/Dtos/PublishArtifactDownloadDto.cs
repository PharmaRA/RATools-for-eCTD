namespace RATools.Application.Publishing.Dtos;

public sealed record PublishArtifactDownloadDto(
    string Name,
    string FileName,
    string Path,
    string ContentType);
