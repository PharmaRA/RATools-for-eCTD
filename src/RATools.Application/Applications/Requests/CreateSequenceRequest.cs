namespace RATools.Application.Applications.Requests;

public sealed record CreateSequenceRequest(string SequenceNumber, string SubmissionType, string Description);
