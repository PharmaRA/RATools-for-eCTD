using RATools.Application.Applications.EctdTemplates;

namespace RATools.Application.Standards;

public sealed class FdaEctd322StandardsProfileProvider : IStandardsProfileProvider
{
    private const string StandardsPageUrl = "https://www.fda.gov/drugs/electronic-regulatory-submission-and-review/ectd-submission-standards-ectd-v322-and-regional-m1";
    private const string EctdOverviewUrl = "https://www.fda.gov/ectd";
    private const string IchSpecificationUrl = "https://admin.ich.org/sites/default/files/inline-files/eCTD_Specification_v3_2_2_0.pdf";
    private readonly BundledStandardsAssetResolver _assets;

    public FdaEctd322StandardsProfileProvider(string? assetRootPath = null)
    {
        _assets = new BundledStandardsAssetResolver(assetRootPath);
    }

    public StandardsProfile GetProfile(string templateKey)
    {
        if (!string.Equals(templateKey, EctdTemplateRegistry.DefaultTemplateKey, StringComparison.OrdinalIgnoreCase))
        {
            throw new StandardsProfileNotFoundException($"Unsupported standards profile '{templateKey}'.");
        }

        return new StandardsProfile(
            EctdTemplateRegistry.DefaultTemplateKey,
            "FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3",
            "FDA CDER/CBER",
            "United States",
            "3.2.2",
            "3.3",
            "1.9",
            "4.5",
            [StandardsPageUrl, EctdOverviewUrl, IchSpecificationUrl],
            [
                _assets.Build(
                    "ich-ectd-3-2-dtd",
                    "ICH eCTD DTD",
                    "DTD",
                    "3.2.2",
                    "reference/dtd/ich-ectd-3-2.dtd",
                    StandardsPageUrl,
                    new DateOnly(2008, 7, 16)),
                _assets.Build(
                    "us-regional-v3-3-dtd",
                    "US Regional DTD",
                    "DTD",
                    "3.3",
                    "reference/dtd/us-regional-v3-3.dtd",
                    StandardsPageUrl,
                    new DateOnly(2015, 12, 1))
            ],
            BackboneXmlProfiles.FdaEctd322UsRegional33);
    }

}
