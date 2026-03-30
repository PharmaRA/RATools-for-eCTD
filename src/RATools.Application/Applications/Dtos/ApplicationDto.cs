namespace RATools.Application.Applications.Dtos;

public sealed record ApplicationDto(
    Guid Id,
    string ApplicationNumber,
    string Region,
    string SponsorName,
    DateTime CreatedUtc,
    IReadOnlyCollection<SequenceDto> Sequences);
