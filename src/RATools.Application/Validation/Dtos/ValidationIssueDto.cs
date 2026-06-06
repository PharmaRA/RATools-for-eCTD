namespace RATools.Application.Validation.Dtos;

public sealed record ValidationIssueDto(
    string Severity,
    string Code,
    string Message,
    string? SectionPath = null,
    Guid? DocumentId = null,
    Guid? PlacementId = null);
