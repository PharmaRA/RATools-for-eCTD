namespace RATools.Application.Validation.Dtos;

public sealed record ValidationReportDto(
    Guid ApplicationId,
    string SequenceNumber,
    bool IsValid,
    IReadOnlyCollection<ValidationIssueDto> Issues);
