namespace RATools.Application.Applications.EctdTemplates;

public sealed class EctdTemplateNotFoundException(string message) : Exception(message);

public static class EctdTemplateRegistry
{
    public const string DefaultTemplateKey = "us-fda-ectd-3.2.2";
    public const string EuTemplateKey = "eu-ectd-3.2.2";

    public static readonly EctdTemplateDefinition Default = new(
        DefaultTemplateKey,
        "US FDA eCTD 3.2.2",
        "US",
        "eCTD",
        "3.2.2",
        "fda-ectd-3.2-manual",
        "3.2.2");

    public static readonly EctdTemplateDefinition Eu = new(
        EuTemplateKey,
        "EU eCTD 3.2.2",
        "EU",
        "eCTD",
        "3.2.2",
        "eu-ectd-3.2.2",
        "EU M1");

    public static IReadOnlyCollection<EctdTemplateDefinition> All { get; } = [Default, Eu];

    public static EctdTemplateDefinition Resolve(string key)
    {
        var template = All.SingleOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
        return template ?? throw new EctdTemplateNotFoundException($"Unsupported eCTD template '{key}'.");
    }
}
