namespace RATools.Infrastructure.Validation;

public sealed class ValidationProfileOptions
{
    public const string SectionName = "ValidationProfile";

    public string Name { get; set; } = "default-v1";

    public string Mode { get; set; } = "strict";
}
