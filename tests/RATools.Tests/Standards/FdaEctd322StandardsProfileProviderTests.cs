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
}
