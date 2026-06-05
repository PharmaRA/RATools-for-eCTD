namespace RATools.Application.Publishing.Dtos;

public sealed record PublishArtifactEvidenceDto(
    string Role,
    string? RelativePath,
    string? Path,
    bool Exists,
    long SizeBytes,
    bool? ZipEntryPresent,
    string Source);
