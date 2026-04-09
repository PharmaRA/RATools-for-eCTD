namespace RATools.Application.Validation.Dtos;

public sealed record ValidationSectionMatchDto(
    string SectionPath,
    bool IsValid,
    bool IsStandard,
    string? MatchedPrefix,
    string? Reason);
