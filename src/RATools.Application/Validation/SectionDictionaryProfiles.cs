using RATools.Application.Validation.Profiles;

namespace RATools.Application.Validation;

public static class SectionDictionaryProfiles
{
    public const string CanonicalUsProfileName = FdaEctd322.ProfileName;

    public const string LegacyDefaultProfileName = "default-v1";

    public static readonly SectionDictionaryProfile FdaEctd32 = FdaEctd322.ToProfile();

    public static readonly SectionDictionaryProfile FdaRegional33 = FdaEctd32;

    public static readonly SectionDictionaryProfile Default = FdaEctd32;

    public static string NormalizeProfileName(string? profileName)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return CanonicalUsProfileName;
        }

        var normalized = profileName.Trim();
        if (string.Equals(normalized, LegacyDefaultProfileName, StringComparison.OrdinalIgnoreCase))
        {
            return CanonicalUsProfileName;
        }

        if (string.Equals(normalized, CanonicalUsProfileName, StringComparison.OrdinalIgnoreCase))
        {
            return CanonicalUsProfileName;
        }

        return normalized;
    }

    public static SectionDictionaryProfile ResolveByName(string? profileName)
    {
        var normalizedName = NormalizeProfileName(profileName);
        if (string.Equals(normalizedName, CanonicalUsProfileName, StringComparison.Ordinal))
        {
            return FdaEctd32;
        }

        return Default;
    }

    public static SectionDictionaryProfile ResolveByRegion(string region)
    {
        if (string.Equals(region.Trim(), "US", StringComparison.OrdinalIgnoreCase))
        {
            return FdaEctd32;
        }

        return Default;
    }
}
