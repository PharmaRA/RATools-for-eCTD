namespace RATools.Application.Applications.Dtos;

public sealed record SequenceDto(
    string SequenceNumber,
    string SubmissionType,
    string Description,
    DateTime CreatedUtc);
