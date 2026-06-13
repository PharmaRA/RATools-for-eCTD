namespace RATools.Application.Validation.Dtos;

public sealed record PublishReadinessFindingDto(
    string Source,
    string Severity,
    string Code,
    string Message,
    string Category,
    string RecommendedAction,
    string? FieldName = null,
    string? SectionPath = null,
    Guid? DocumentId = null,
    Guid? PlacementId = null);
