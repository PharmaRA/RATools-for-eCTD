namespace RATools.Application.Abstractions.Persistence;

public sealed record PublishJobHistoryQuery(
    Guid ApplicationId,
    string? SequenceNumber,
    string? Status,
    DateTime? CreatedFromUtc,
    DateTime? CreatedToUtc,
    int Page,
    int PageSize);
