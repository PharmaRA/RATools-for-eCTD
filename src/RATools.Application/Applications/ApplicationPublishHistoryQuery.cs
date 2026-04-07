namespace RATools.Application.Applications;

public sealed record ApplicationPublishHistoryQuery(
    string? SequenceNumber,
    int Page = 1,
    int PageSize = 20,
    string? Status = null,
    DateTime? CreatedFromUtc = null,
    DateTime? CreatedToUtc = null);
