namespace RATools.Application.Applications.Dtos;

public sealed record ApplicationImportIssueDto(
    string Severity,
    string Code,
    string? SequenceNumber,
    string Message);
