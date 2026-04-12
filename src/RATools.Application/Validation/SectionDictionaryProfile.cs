namespace RATools.Application.Validation;

public sealed class SectionDictionaryProfile
{
    public required string Name { get; init; }

    public required IReadOnlyDictionary<string, SectionDictionaryEntry> ByElementName { get; init; }

    public required IReadOnlyDictionary<string, SectionDictionaryEntry[]> BySectionPath { get; init; }
}
