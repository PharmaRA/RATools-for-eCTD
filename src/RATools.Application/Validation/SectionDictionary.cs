namespace RATools.Application.Validation;

public sealed class SectionDictionary
{
    private static readonly string[] HighFrequencyPrefixes =
    [
        "m1",
        "m2.3",
        "m2.5",
        "m2",
        "m3.2.a",
        "m3.2.p",
        "m3.2.r",
        "m3.2.s",
        "m3.2",
        "m4.2",
        "m4.3",
        "m4",
        "m5.1",
        "m5.2",
        "m5.3.1",
        "m5.3.2",
        "m5.3.3",
        "m5.3.4",
        "m5.3.5",
        "m5.3.5.1",
        "m5.3.5.2",
        "m5.3.5.3",
        "m5.3.5.4",
        "m5.3"
    ];

    public SectionDictionaryMatch Classify(string ctdSection)
    {
        if (string.IsNullOrWhiteSpace(ctdSection))
        {
            return new SectionDictionaryMatch(false, false, null, "INVALID_SECTION_PATH");
        }

        if (ctdSection.Contains("..", StringComparison.Ordinal) ||
            ctdSection.StartsWith(".", StringComparison.Ordinal) ||
            ctdSection.EndsWith(".", StringComparison.Ordinal))
        {
            return new SectionDictionaryMatch(false, false, null, "INVALID_SECTION_PATH");
        }

        var parts = ctdSection.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !IsValidModulePrefix(parts[0]) || parts.Skip(1).Any(part => !IsValidSectionSegment(part)))
        {
            return new SectionDictionaryMatch(false, false, null, "INVALID_SECTION_PATH");
        }

        var match = HighFrequencyPrefixes
            .Where(prefix => ctdSection.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                             || ctdSection.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(prefix => prefix.Length)
            .FirstOrDefault();

        if (match is not null)
        {
            return new SectionDictionaryMatch(true, true, match, null);
        }

        return new SectionDictionaryMatch(true, false, null, "NON_STANDARD_SECTION_PATTERN");
    }

    private static bool IsValidModulePrefix(string sectionSegment)
    {
        return sectionSegment.Equals("m1", StringComparison.OrdinalIgnoreCase)
               || sectionSegment.Equals("m2", StringComparison.OrdinalIgnoreCase)
               || sectionSegment.Equals("m3", StringComparison.OrdinalIgnoreCase)
               || sectionSegment.Equals("m4", StringComparison.OrdinalIgnoreCase)
               || sectionSegment.Equals("m5", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsValidSectionSegment(string part)
    {
        if (int.TryParse(part, out _))
        {
            return true;
        }

        return part.Equals("p", StringComparison.OrdinalIgnoreCase)
               || part.Equals("s", StringComparison.OrdinalIgnoreCase)
               || part.Equals("r", StringComparison.OrdinalIgnoreCase)
               || part.Equals("a", StringComparison.OrdinalIgnoreCase);
    }
}
