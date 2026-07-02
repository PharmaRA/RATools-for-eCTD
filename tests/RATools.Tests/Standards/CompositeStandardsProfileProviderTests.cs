using RATools.Application.Standards;

namespace RATools.Tests.Standards;

public sealed class CompositeStandardsProfileProviderTests
{
    [Fact]
    public void GetProfile_ReturnsProfileFromMatchingProvider()
    {
        var fdaProfile = CreateProfile("us-fda-ectd-3.2.2", "FDA");
        var provider = new CompositeStandardsProfileProvider(
            [
                new StubStandardsProfileProvider("eu-ectd-3.2.2", CreateProfile("eu-ectd-3.2.2", "EU")),
                new StubStandardsProfileProvider("us-fda-ectd-3.2.2", fdaProfile)
            ]);

        var profile = provider.GetProfile("us-fda-ectd-3.2.2");

        Assert.Same(fdaProfile, profile);
    }

    [Fact]
    public void GetProfile_ThrowsWhenNoProviderSupportsTemplate()
    {
        var provider = new CompositeStandardsProfileProvider(
            [new StubStandardsProfileProvider("us-fda-ectd-3.2.2", CreateProfile("us-fda-ectd-3.2.2", "FDA"))]);

        void Act() => provider.GetProfile("unknown-template");

        var exception = Assert.Throws<StandardsProfileNotFoundException>(Act);

        Assert.Contains("unknown-template", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetProfile_PropagatesNonNotFoundProviderErrors()
    {
        var provider = new CompositeStandardsProfileProvider([new ThrowingStandardsProfileProvider()]);

        void Act() => provider.GetProfile("us-fda-ectd-3.2.2");

        var exception = Assert.Throws<StandardsAssetMissingException>(Act);

        Assert.Contains("asset missing", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetProfile_ReturnsEuProfileFromEuProvider()
    {
        var provider = new CompositeStandardsProfileProvider(
            [
                new FdaEctd322StandardsProfileProvider(),
                new EuEctd322StandardsProfileProvider()
            ]);

        var profile = provider.GetProfile("eu-ectd-3.2.2");

        Assert.Equal("eu-ectd-3.2.2", profile.TemplateKey);
        Assert.Equal("EU", profile.Region);
        Assert.Equal("m1/eu/eu-regional.xml", profile.BackboneXml?.Regional.RelativePath);
        Assert.Contains(profile.Assets, asset => asset.LocalRelativePath == "reference/dtd/eu-regional.dtd");
    }

    private static StandardsProfile CreateProfile(string templateKey, string region)
        => new(
            templateKey,
            $"{region} Profile",
            region,
            region,
            "3.2.2",
            "3.3",
            "1.0",
            "1.0",
            [],
            [],
            BackboneXmlProfiles.FdaEctd322UsRegional33);

    private sealed class StubStandardsProfileProvider(string supportedTemplateKey, StandardsProfile profile) : IStandardsProfileProvider
    {
        public StandardsProfile GetProfile(string templateKey)
            => string.Equals(templateKey, supportedTemplateKey, StringComparison.OrdinalIgnoreCase)
                ? profile
                : throw new StandardsProfileNotFoundException($"Unsupported standards profile '{templateKey}'.");
    }

    private sealed class ThrowingStandardsProfileProvider : IStandardsProfileProvider
    {
        public StandardsProfile GetProfile(string templateKey)
            => throw new StandardsAssetMissingException("asset missing");
    }
}
