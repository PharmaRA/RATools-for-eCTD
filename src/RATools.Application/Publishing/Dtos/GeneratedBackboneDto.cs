namespace RATools.Application.Publishing.Dtos;

public sealed record GeneratedBackboneDto(
    Guid ApplicationId,
    string SequenceNumber,
    string FileName,
    string FilePath,
    string PackagePath,
    string XmlContent);
