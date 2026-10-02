using RATools.Application.PackageValidation;

namespace RATools.Tests.PackageValidation;

public sealed class PackageLogicalPathTests
{
    [Theory]
    [InlineData("0001/index.xml", "../0000/index.xml#alpha-spec-v1", "0000/index.xml", "alpha-spec-v1")]
    [InlineData("0001/m1/us/us-regional.xml", "../../../0000/index.xml#leaf", "0000/index.xml", "leaf")]
    [InlineData("0001/index.xml", "m3/Drug%20A.pdf#named%20destination", "0001/m3/Drug A.pdf", "named destination")]
    [InlineData("0001/index.xml", "m3/%252e%252e/file.pdf", "0001/m3/%2e%2e/file.pdf", null)]
    [InlineData("0001/index.xml", "m3/file%23name.pdf", "0001/m3/file#name.pdf", null)]
    [InlineData("0001/index.xml", "#same-document", "0001/index.xml", "same-document")]
    public void ReferencesDecodeExactlyOnceAndResolveFromContainingBackbone(string source, string reference, string path, string? fragment)
    {
        var result = PackageLogicalPath.ResolveReference(source, reference);
        Assert.Equal(path, result.LogicalPath);
        Assert.Equal(fragment, result.Fragment);
    }

    [Theory]
    [InlineData("../../escape.pdf")]
    [InlineData("../%2e%2e/escape.pdf")]
    [InlineData("m3/a%2fb.pdf")]
    [InlineData("m3/a%5cb.pdf")]
    [InlineData("/0000/index.xml")]
    [InlineData("file:///outside.pdf")]
    [InlineData("C:/outside.pdf")]
    [InlineData("m3/file%ZZ.pdf")]
    [InlineData("m3/file%ff.pdf")]
    [InlineData("m3/file%00.pdf")]
    public void UnsafeReferencesAreRejected(string reference) =>
        Assert.ThrowsAny<ArgumentException>(() => PackageLogicalPath.ResolveReference("0001/index.xml", reference));

    [Fact]
    public void QueryFragmentAndExternalUrlHaveSeparateMeaning()
    {
        var local = PackageLogicalPath.ResolveReference("0001/index.xml", "m3/file.pdf?x=%2f#named%20target");
        Assert.Equal("0001/m3/file.pdf", local.LogicalPath);
        Assert.Equal("x=%2f", local.Query);
        Assert.Equal("named target", local.Fragment);
        var external = PackageLogicalPath.ResolveReference("0001/index.xml", "https://example.invalid/document.pdf#page=1");
        Assert.Null(external.LogicalPath);
        Assert.Equal("https://example.invalid/document.pdf#page=1", external.ExternalUri);
    }

    [Fact]
    public void ResourceLimitChangesInvalidateReuseIdentity()
    {
        var limits = new PackageReadLimits();
        Assert.NotEqual(limits.Digest(), (limits with { MaxEntries = 20 }).Digest());
        Assert.NotEqual(limits.Digest(), (limits with { MaxElapsedSeconds = 20 }).Digest());
        Assert.Throws<ArgumentException>(() => (limits with { MaxCompressionRatio = 0 }).Digest());
    }
}
