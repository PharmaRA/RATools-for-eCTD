namespace RATools.Application.Validation.Dtos;

public sealed record ValidationReportDto(
    Guid ApplicationId,
    string SequenceNumber,
    string ValidationProfile,
    bool IsValid,
    IReadOnlyCollection<ValidationIssueDto> Issues);
