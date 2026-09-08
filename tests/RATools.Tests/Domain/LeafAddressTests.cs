using RATools.Domain.Ctd;

namespace RATools.Tests.Domain;

[Trait("Category", "PathSecurity")]
public sealed class LeafAddressTests
{
    [Fact]
    public void SameXmlIdInDifferentBackbonesOrSequencesIsADifferentAddress()
    {
        var app = Guid.NewGuid();
        var ich = new LeafAddress(app, "0000", "index.xml", "overview");
        Assert.Equal(ich, new LeafAddress(app, "0000", "index.xml", "overview"));
        Assert.NotEqual(ich, new LeafAddress(app, "0000", "m1/us/us-regional.xml", "overview"));
        Assert.NotEqual(ich, new LeafAddress(app, "0001", "index.xml", "overview"));
        Assert.NotEqual(ich, new LeafAddress(app, "0000", "index.xml", "Overview"));
        Assert.NotEqual(ich, new LeafAddress(Guid.NewGuid(), "0000", "index.xml", "overview"));
    }

    [Theory]
    [InlineData("index.xml", "index.xml", "../0000/index.xml#alpha")]
    [InlineData("m1/us/us-regional.xml", "index.xml", "../../../0000/index.xml#alpha")]
    [InlineData("index.xml", "m1/us/us-regional.xml", "../0000/m1/us/us-regional.xml#alpha")]
    [InlineData("m1/us/us-regional.xml", "m1/us/us-regional.xml", "../../../0000/m1/us/us-regional.xml#alpha")]
    public void HistoricalReferencesUseBothBackbonePaths(string sourcePath, string targetPath, string expected)
    {
        var app = Guid.NewGuid();
        var source = new LeafAddress(app, "0001", sourcePath, "new-leaf");
        var target = new LeafAddress(app, "0000", targetPath, "alpha");
        Assert.Equal(expected, target.BuildModifiedFileHref(source));
    }

    [Theory]
    [InlineData("../index.xml")]
    [InlineData("/index.xml")]
    [InlineData("C:/index.xml")]
    [InlineData("m1\\us\\index.xml")]
    [InlineData("index.xml#id")]
    [InlineData("m1//index.xml")]
    [InlineData("../other.xml")]
    [InlineData("m1/CON.xml")]
    public void NonlogicalBackbonePathsAreRejected(string path) =>
        Assert.ThrowsAny<ArgumentException>(() => new LeafAddress(Guid.NewGuid(), "0000", path, "leaf"));

    [Fact]
    public void ReferencesCannotCrossApplicationsOrPointToFutureSequences()
    {
        var source = new LeafAddress(Guid.NewGuid(), "0001", "index.xml", "leaf");
        Assert.Equal("LifecycleApplicationMismatch", Assert.Throws<CtdNodeConstraintException>(() =>
            new LeafAddress(Guid.NewGuid(), "0000", "index.xml", "leaf").BuildModifiedFileHref(source)).Code);
        Assert.Equal("LifecycleTargetInFuture", Assert.Throws<CtdNodeConstraintException>(() =>
            new LeafAddress(source.ApplicationId, "0002", "index.xml", "leaf").BuildModifiedFileHref(source)).Code);
        Assert.Equal("index.xml#old", new LeafAddress(source.ApplicationId, "0001", "index.xml", "old").BuildModifiedFileHref(source));
    }
}
