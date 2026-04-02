namespace RATools.Application.Publishing.Dtos;

public sealed record PublishArtifactsDto(
    Guid PublishJobId,
    Guid ApplicationId,
    string SequenceNumber,
    IReadOnlyCollection<PublishArtifactDto> Artifacts);
