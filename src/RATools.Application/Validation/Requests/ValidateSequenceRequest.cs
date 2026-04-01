namespace RATools.Application.Validation.Requests;

public sealed record ValidateSequenceRequest(Guid ApplicationId, string SequenceNumber);
