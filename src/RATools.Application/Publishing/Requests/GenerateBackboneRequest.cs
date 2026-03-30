namespace RATools.Application.Publishing.Requests;

public sealed record GenerateBackboneRequest(Guid ApplicationId, string SequenceNumber);
