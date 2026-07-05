using RATools.Application.Publishing;

namespace RATools.Tests.Publishing;

public sealed class PublishArtifactDescriptorCatalogTests
{
    [Fact]
    public void All_ListsSupportedArtifactsInResponseOrder()
    {
        var artifactNames = PublishArtifactDescriptorCatalog.All.Select(x => x.Name).ToArray();

        Assert.Equal(["BackboneXml", "PublishReport", "PackageZip"], artifactNames);
    }

    [Fact]
    public void Find_MatchesArtifactNameCaseInsensitively()
    {
        var descriptor = PublishArtifactDescriptorCatalog.Find("publishreport");

        Assert.NotNull(descriptor);
        Assert.Equal("PublishReport", descriptor!.Name);
        Assert.Equal("application/json", descriptor.ContentType);
        Assert.Null(PublishArtifactDescriptorCatalog.Find("unknown"));
    }
}
