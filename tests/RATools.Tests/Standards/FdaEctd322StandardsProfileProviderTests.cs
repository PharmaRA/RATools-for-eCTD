using Microsoft.Extensions.DependencyInjection;
using RATools.Application;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Standards;

namespace RATools.Tests.Standards;

public sealed class FdaEctd322StandardsProfileProviderTests
{
    [Fact]
    public void GetProfile_ReturnsOfficialBaselineMetadata()
    {
        var provider = new FdaEctd322StandardsProfileProvider();

        var profile = provider.GetProfile(EctdTemplateRegistry.DefaultTemplateKey);

        Assert.Equal("us-fda-ectd-3.2.2", profile.TemplateKey);
        Assert.Equal("FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3", profile.DisplayName);
        Assert.Equal("FDA CDER/CBER", profile.RegulatoryAgency);
        Assert.Equal("United States", profile.Region);
        Assert.Equal("3.2.2", profile.IchEctdVersion);
        Assert.Equal("3.3", profile.UsRegionalModule1Version);
        Assert.Equal("1.9", profile.TechnicalConformanceGuideVersion);
        Assert.Equal("4.5", profile.ValidationCriteriaVersion);
        Assert.Contains(profile.OfficialReferences, x => x.Contains("fda.gov", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GetProfile_IncludesBundledDtdAssetsWithChecksums()
    {
        var provider = new FdaEctd322StandardsProfileProvider();

        var profile = provider.GetProfile(EctdTemplateRegistry.DefaultTemplateKey);

        var ichDtd = Assert.Single(profile.Assets, x => x.Key == "ich-ectd-3-2-dtd");
        Assert.Equal("ICH eCTD DTD", ichDtd.DisplayName);
        Assert.Equal("DTD", ichDtd.Category);
        Assert.Equal("3.2.2", ichDtd.Version);
        Assert.Equal("reference/dtd/ich-ectd-3-2.dtd", ichDtd.LocalRelativePath);
        Assert.StartsWith("https://", ichDtd.SourceUrl, StringComparison.Ordinal);
        Assert.Matches("^[a-f0-9]{64}$", ichDtd.Sha256);

        var regionalDtd = Assert.Single(profile.Assets, x => x.Key == "us-regional-v3-3-dtd");
        Assert.Equal("US Regional DTD", regionalDtd.DisplayName);
        Assert.Equal("3.3", regionalDtd.Version);
        Assert.Equal("reference/dtd/us-regional-v3-3.dtd", regionalDtd.LocalRelativePath);
        Assert.Matches("^[a-f0-9]{64}$", regionalDtd.Sha256);
    }

    [Fact]
    public void GetProfile_IncludesFdaBackboneXmlContract()
    {
        var provider = new FdaEctd322StandardsProfileProvider();

        var profile = provider.GetProfile(EctdTemplateRegistry.DefaultTemplateKey);
        Assert.NotNull(profile.BackboneXml);
        var backboneXml = profile.BackboneXml!;

        Assert.Equal("ectd:ectd", backboneXml.Ich.DocumentTypeName);
        Assert.Equal("ectd", backboneXml.Ich.RootElementName);
        Assert.Equal("http://www.ich.org/ectd", backboneXml.Ich.Namespace);
        Assert.Equal("3.2", backboneXml.Ich.DtdVersion);
        Assert.Equal("util/dtd/ich-ectd-3-2.dtd", backboneXml.Ich.DtdSystemId);
        Assert.Equal("fda-regional:fda-regional", backboneXml.Regional.DocumentTypeName);
        Assert.Equal("fda-regional", backboneXml.Regional.RootElementName);
        Assert.Equal("http://www.ich.org/fda", backboneXml.Regional.Namespace);
        Assert.Equal("3.3", backboneXml.Regional.DtdVersion);
        Assert.Equal("../../util/dtd/us-regional-v3-3.dtd", backboneXml.Regional.DtdSystemId);
        Assert.Equal("m1/us/us-regional.xml", backboneXml.Regional.RelativePath);
    }

    [Fact]
    public void GetProfile_ThrowsForUnsupportedTemplate()
    {
        var provider = new FdaEctd322StandardsProfileProvider();

        var exception = Assert.Throws<StandardsProfileNotFoundException>(() => provider.GetProfile("eu-ectd-3.2.2"));

        Assert.Contains("Unsupported standards profile", exception.Message);
    }

    [Fact]
    public void GetProfile_FailsWhenBundledDtdAssetIsMissing()
    {
        var provider = new FdaEctd322StandardsProfileProvider(assetRootPath: Path.Combine(Path.GetTempPath(), $"missing-assets-{Guid.NewGuid():N}"));

        var exception = Assert.Throws<StandardsAssetMissingException>(() => provider.GetProfile(EctdTemplateRegistry.DefaultTemplateKey));

        Assert.Contains("Bundled standards asset", exception.Message);
        Assert.Contains("ich-ectd-3-2.dtd", exception.Message);
    }

    [Fact]
    public void AddApplication_RegistersStandardsProfileProvider()
    {
        var services = new ServiceCollection();

        services.AddApplication();
        using var provider = services.BuildServiceProvider();

        var standardsProvider = provider.GetRequiredService<IStandardsProfileProvider>();
        var profile = standardsProvider.GetProfile(EctdTemplateRegistry.DefaultTemplateKey);

        Assert.Equal("FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3", profile.DisplayName);
    }
}
