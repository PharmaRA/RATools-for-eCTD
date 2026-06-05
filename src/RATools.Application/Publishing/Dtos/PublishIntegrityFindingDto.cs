namespace RATools.Application.Publishing.Dtos;

public sealed record PublishIntegrityFindingDto(
    string Severity,
    string Type,
    string? Path,
    string Message);
