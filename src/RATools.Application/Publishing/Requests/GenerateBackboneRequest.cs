namespace RATools.Application.Publishing.Requests;

public sealed record GenerateBackboneRequest(
    Guid ApplicationId,
    string SequenceNumber,
    Guid PublishJobId,
    string OutputDirectoryPath,
    string ReportFileName,
    string PackageFileName);
