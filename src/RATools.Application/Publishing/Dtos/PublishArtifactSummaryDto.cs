namespace RATools.Application.Publishing.Dtos;

public sealed record PublishArtifactSummaryDto(
    int FileCount,
    long TotalSizeBytes,
    long PackageSizeBytes);
