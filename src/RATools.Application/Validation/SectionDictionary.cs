namespace RATools.Application.Validation;

public sealed class SectionDictionary
{
    private readonly SectionDictionaryProfile _profile;

    public SectionDictionary()
        : this(SectionDictionaryProfiles.Default)
    {
    }

    public SectionDictionary(SectionDictionaryProfile profile)
    {
        _profile = profile;
    }

    public SectionDictionaryMatch Classify(string ctdSection)
    {
        if (!TryNormalizeSectionPath(ctdSection, out var normalizedPath))
        {
            return new SectionDictionaryMatch(false, false, null, "INVALID_SECTION_PATH", null, null);
        }

        if (_profile.BySectionPath.TryGetValue(normalizedPath, out var exactMatches) && exactMatches.Length > 0)
        {
            var first = exactMatches.OrderBy(x => x.ElementName, StringComparer.OrdinalIgnoreCase).First();
            return new SectionDictionaryMatch(true, true, normalizedPath, null, normalizedPath, first.ElementName);
        }

        return new SectionDictionaryMatch(true, false, null, "NON_STANDARD_SECTION_PATTERN", normalizedPath, null);
    }

    public SectionDictionaryMatch ClassifyElementName(string elementName)
    {
        if (string.IsNullOrWhiteSpace(elementName))
        {
            return new SectionDictionaryMatch(false, false, null, "INVALID_SECTION_PATH", null, null);
        }

        var normalizedPath = NormalizeElementNameToSectionPath(elementName);
        if (normalizedPath is null)
        {
            return new SectionDictionaryMatch(false, false, null, "INVALID_SECTION_PATH", null, null);
        }

        if (_profile.ByElementName.TryGetValue(elementName, out var entry))
        {
            return new SectionDictionaryMatch(true, true, entry.SectionPath, null, normalizedPath, entry.ElementName);
        }

        return new SectionDictionaryMatch(true, false, null, "NON_STANDARD_SECTION_PATTERN", normalizedPath, elementName);
    }

    private static bool TryNormalizeSectionPath(string ctdSection, out string normalizedPath)
    {
        normalizedPath = string.Empty;

        if (string.IsNullOrWhiteSpace(ctdSection) ||
            ctdSection.Contains("..", StringComparison.Ordinal) ||
            ctdSection.StartsWith('.') ||
            ctdSection.EndsWith('.'))
        {
            return false;
        }

        var parts = ctdSection.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0 || !IsValidModulePrefix(parts[0]) || parts.Skip(1).Any(part => !IsValidSectionSegment(part)))
        {
            return false;
        }

        normalizedPath = string.Join('.', parts.Select(x => x.ToLowerInvariant()));
        return true;
    }

    public static string? NormalizeElementNameToSectionPath(string elementName)
    {
        var tokens = elementName.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var parts = new List<string>();

        foreach (var token in tokens)
        {
            if (parts.Count == 0)
            {
                if (!IsValidModulePrefix(token))
                {
                    break;
                }

                parts.Add(token.ToLowerInvariant());
                continue;
            }

            if (IsValidSectionSegment(token))
            {
                parts.Add(token.ToLowerInvariant());
                continue;
            }

            break;
        }

        return parts.Count == 0 ? null : string.Join('.', parts);
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
