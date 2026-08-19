using RATools.Application.Applications.EctdTemplates;

namespace RATools.Application.Standards;

public sealed class EuEctd322StandardsProfileProvider : IStandardsProfileProvider
{
    private const string EmaEctdUrl = "https://esubmission.ema.europa.eu/eumodule1/index.htm";
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
            "EU eCTD v3.2.2 + EU M1 v3.1.1",
            "European Medicines Agency",
            "EU",
            "3.2.2",
            "3.1.1",
            "6.0.1",
            "8.2",
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
                    "EU M1 Regional DTD",
                    "DTD",
                    "3.1",
                    "reference/eu-m1/3.1.1/util/dtd/eu-regional.dtd",
                    EmaEctdUrl,
                    new DateOnly(2025, 12, 1)),
                _assets.Build(
                    "eu-envelope-module",
                    "EU M1 Envelope DTD Module",
                    "DTD",
                    "3.1",
                    "reference/eu-m1/3.1.1/util/dtd/eu-envelope.mod",
                    EmaEctdUrl,
                    new DateOnly(2025, 12, 1)),
                _assets.Build(
                    "eu-leaf-module",
                    "EU M1 Leaf DTD Module",
                    "DTD",
                    "3.1",
                    "reference/eu-m1/3.1.1/util/dtd/eu-leaf.mod",
                    EmaEctdUrl,
                    new DateOnly(2025, 12, 1)),
                _assets.Build(
                    "eu-regional-stylesheet",
                    "EU M1 Regional Stylesheet",
                    "XSL",
                    "3.1",
                    "reference/eu-m1/3.1.1/util/style/eu-regional.xsl",
                    EmaEctdUrl,
                    new DateOnly(2025, 12, 1)),
                _assets.Build(
                    "ectd-stylesheet",
                    "eCTD Stylesheet",
                    "XSL",
                    "2.0",
                    "reference/eu-m1/3.1.1/util/style/ectd-2-0.xsl",
                    EmaEctdUrl,
                    new DateOnly(2025, 12, 1))
            ],
            BackboneXmlProfiles.EuEctd322Regional,
            new StandardsLifecycle(
                "https://esubmission.ema.europa.eu/eumodule1/index.htm",
                "3.1.1",
                new DateOnly(2025, 12, 1),
                null,
                StandardsLifecycleStatus.AcquiredNotActive,
                "Retain each published EU M1 snapshot for historical lifecycle validation. Mark a snapshot retired only after EMA publishes a replacement with an announced effective date; never silently rewrite an existing application lifecycle."));
    }

}
