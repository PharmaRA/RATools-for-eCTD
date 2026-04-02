namespace RATools.Application.Publishing.Dtos;

public sealed record PublishArtifactDto(
    string Name,
    string Type,
    string? Path,
    bool Exists,
    long SizeBytes);
