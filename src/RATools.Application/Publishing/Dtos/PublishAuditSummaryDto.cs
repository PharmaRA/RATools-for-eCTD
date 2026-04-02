namespace RATools.Application.Publishing.Dtos;

public sealed record PublishAuditSummaryDto(
    int PublishJobEventCount,
    int ValidationEventCount,
    string? LatestPublishJobAction,
    DateTime? LatestPublishJobEventUtc);
