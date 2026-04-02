namespace RATools.Application.Publishing.Dtos;

public sealed record GeneratedBackboneDto(
    Guid ApplicationId,
    string SequenceNumber,
    string FileName,
    string FilePath,
    string ReportPath,
    string PackagePath,
    string XmlContent);
