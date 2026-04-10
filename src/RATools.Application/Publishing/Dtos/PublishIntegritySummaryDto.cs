namespace RATools.Application.Publishing.Dtos;

public sealed record PublishIntegritySummaryDto(
    bool IsConsistent,
    int MissingFilesCount,
    int MissingZipEntriesCount,
    int MismatchedArtifactsCount);
