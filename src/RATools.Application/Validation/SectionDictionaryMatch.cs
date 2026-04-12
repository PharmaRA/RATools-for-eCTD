namespace RATools.Application.Validation;

public sealed record SectionDictionaryMatch(
    bool IsValid,
    bool IsStandard,
    string? MatchedPrefix,
    string? Reason,
    string? SectionPath,
    string? MatchedElementName);
