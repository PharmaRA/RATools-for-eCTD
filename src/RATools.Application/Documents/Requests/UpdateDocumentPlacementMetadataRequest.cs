namespace RATools.Application.Documents.Requests;

public sealed record UpdateDocumentPlacementMetadataRequest(string? Title, string Operation, string FileNamePrefix);
