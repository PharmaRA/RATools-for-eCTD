using Microsoft.Extensions.Options;
using RATools.Application.Validation;

namespace RATools.Infrastructure.Validation;

public sealed class ConfigurationValidationProfileProvider(IOptions<ValidationProfileOptions> options) : IValidationProfileProvider
{
    public string ProfileName => string.IsNullOrWhiteSpace(options.Value.Name) ? "default-v1" : options.Value.Name.Trim();

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
