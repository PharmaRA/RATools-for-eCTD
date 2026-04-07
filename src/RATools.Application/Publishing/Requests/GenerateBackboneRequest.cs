namespace RATools.Application.Publishing.Requests;

public sealed record GenerateBackboneRequest(
    Guid ApplicationId,
    string SequenceNumber,
    string ReportFileName,
    string PackageFileName);
