namespace RATools.Application.Applications.Dtos;

public sealed record ApplicationDto(
    Guid Id,
    string ApplicationNumber,
    string Region,
    string SponsorName,
    string WorkingDirectoryPath,
    DateTime CreatedUtc,
    string EctdTemplateKey,
    string EctdTemplateDisplayName,
    IReadOnlyCollection<SequenceDto> Sequences);
