namespace RATools.Application.Documents.Dtos;

public sealed record DocumentPlacementDto(
    Guid Id,
    Guid DocumentId,
    Guid ApplicationId,
    string SequenceNumber,
    string CtdSection,
    string Operation,
    string? Title,
    Guid? LifecycleTargetPlacementId,
    DateTime CreatedUtc);
