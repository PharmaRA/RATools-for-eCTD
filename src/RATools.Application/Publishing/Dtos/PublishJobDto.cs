namespace RATools.Application.Publishing.Dtos;

public sealed record PublishJobDto(
    Guid Id,
    Guid ApplicationId,
    string SequenceNumber,
    string Status,
    string? OutputPath,
    DateTime CreatedUtc,
    DateTime? CompletedUtc,
    string? FailureReason);
