namespace RATools.Application.Publishing.Dtos;

public sealed record PublishIntegrityEvidenceDto(
    IReadOnlyCollection<PublishArtifactEvidenceDto> Artifacts,
    IReadOnlyCollection<PublishIntegrityFindingDto> Findings);
