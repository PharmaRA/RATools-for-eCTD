namespace RATools.Application.Standards;

public sealed class CompositeStandardsProfileProvider(IEnumerable<IStandardsProfileProvider> providers)
    : IStandardsProfileProvider
{
    private readonly IStandardsProfileProvider[] _providers = providers.ToArray();

    public StandardsProfile GetProfile(string templateKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);

        foreach (var provider in _providers)
        {
            try
            {
                return provider.GetProfile(templateKey);
            }
            catch (StandardsProfileNotFoundException)
            {
            }
        }

        throw new StandardsProfileNotFoundException($"Unsupported standards profile '{templateKey}'.");
    }
}
