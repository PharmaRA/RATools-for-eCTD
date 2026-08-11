namespace RATools.Application.Publishing.Dtos;

public sealed record PublishJobDto(
    Guid Id,
    Guid ApplicationId,
    string SequenceNumber,
    string Status,
    string? OutputPath,
    string? PackagePath,
    DateTime CreatedUtc,
    DateTime? CompletedUtc,
    string? FailureReason,
    string? IdempotencyKey = null,
    int AttemptCount = 0,
    DateTime? NextAttemptUtc = null);
