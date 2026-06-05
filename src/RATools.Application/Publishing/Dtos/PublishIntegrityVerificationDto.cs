namespace RATools.Application.Publishing.Dtos;

public sealed record PublishIntegrityVerificationDto(
    PublishIntegritySummaryDto Summary,
    PublishIntegrityEvidenceDto Evidence);
