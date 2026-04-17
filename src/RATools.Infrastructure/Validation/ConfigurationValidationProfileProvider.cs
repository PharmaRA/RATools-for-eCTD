using Microsoft.Extensions.Options;
using RATools.Application.Validation;

namespace RATools.Infrastructure.Validation;

public sealed class ConfigurationValidationProfileProvider(IOptions<ValidationProfileOptions> options) : IValidationProfileProvider
{
    public string ProfileName => SectionDictionaryProfiles.NormalizeProfileName(options.Value.Name);

    public ValidationMode Mode
    {
        get
        {
            var mode = options.Value.Mode;
            if (string.Equals(mode, "relaxed", StringComparison.OrdinalIgnoreCase))
            {
                return ValidationMode.Relaxed;
            }

            return ValidationMode.Strict;
        }
    }
}
