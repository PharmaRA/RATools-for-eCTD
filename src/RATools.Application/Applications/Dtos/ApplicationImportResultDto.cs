namespace RATools.Application.Applications.Dtos;

public sealed record ApplicationImportResultDto(
    Guid ApplicationId,
    string ApplicationNumber,
    string WorkingDirectoryPath,
    int ImportedSequenceCount,
    int ImportedDocumentCount,
    int ImportedPlacementCount,
    int SkippedSequenceCount,
    int FailedSequenceCount,
    IReadOnlyCollection<ApplicationImportIssueDto> Issues);
