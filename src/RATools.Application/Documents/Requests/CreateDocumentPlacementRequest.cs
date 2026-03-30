namespace RATools.Application.Documents.Requests;

public sealed record CreateDocumentPlacementRequest(
    Guid DocumentId,
    Guid ApplicationId,
    string SequenceNumber,
    string CtdSection,
    string Operation,
    string? Title);
