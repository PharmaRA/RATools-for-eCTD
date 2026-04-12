using RATools.Application.Validation.Profiles;

namespace RATools.Application.Validation;

public static class SectionDictionaryProfiles
{
    public static readonly SectionDictionaryProfile FdaEctd32 = FdaEctd322.ToProfile();

    public static readonly SectionDictionaryProfile FdaRegional33 = FdaEctd32;

    public static readonly SectionDictionaryProfile Default = FdaEctd32;
}
