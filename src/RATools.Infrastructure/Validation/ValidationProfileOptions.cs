using RATools.Application.Validation;

namespace RATools.Infrastructure.Validation;

public sealed class ValidationProfileOptions
{
    public const string SectionName = "ValidationProfile";

    public string Name { get; set; } = SectionDictionaryProfiles.CanonicalUsProfileName;

    public string Mode { get; set; } = "strict";
}
