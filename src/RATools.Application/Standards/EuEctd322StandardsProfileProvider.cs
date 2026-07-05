using RATools.Application.Applications.EctdTemplates;

namespace RATools.Application.Standards;

public sealed class EuEctd322StandardsProfileProvider : IStandardsProfileProvider
{
    private const string EmaEctdUrl = "https://esubmission.ema.europa.eu/ectd/";
    private const string IchSpecificationUrl = "https://admin.ich.org/sites/default/files/inline-files/eCTD_Specification_v3_2_2_0.pdf";
    private readonly BundledStandardsAssetResolver _assets;

    public EuEctd322StandardsProfileProvider(string? assetRootPath = null)
    {
        _assets = new BundledStandardsAssetResolver(assetRootPath);
    }

    public StandardsProfile GetProfile(string templateKey)
    {
        if (!string.Equals(templateKey, EctdTemplateRegistry.EuTemplateKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new StandardsProfileNotFoundException($"Unsupported standards profile '{templateKey}'.");
        }

        return new StandardsProfile(
            EctdTemplateRegistry.EuTemplateKey,
            "EU eCTD v3.2.2 + EU Regional M1",
            "European Medicines Agency",
            "EU",
            "3.2.2",
            "EU M1",
            "EU",
            "EU",
            [EmaEctdUrl, IchSpecificationUrl],
            [
                _assets.Build(
                    "ich-ectd-3-2-dtd",
                    "ICH eCTD DTD",
                    "DTD",
                    "3.2.2",
                    "reference/dtd/ich-ectd-3-2.dtd",
                    IchSpecificationUrl,
                    new DateOnly(2008, 7, 16)),
                _assets.Build(
                    "eu-regional-dtd",
                    "EU Regional DTD",
                    "DTD",
                    "EU M1",
                    "reference/dtd/eu-regional.dtd",
                    EmaEctdUrl,
                    null)
            ],
            BackboneXmlProfiles.EuEctd322Regional);
    }

}
