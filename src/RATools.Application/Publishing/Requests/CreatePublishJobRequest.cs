namespace RATools.Application.Publishing.Requests;

public sealed record CreatePublishJobRequest(Guid ApplicationId, string SequenceNumber, string OutputDirectoryPath);
