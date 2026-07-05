using RATools.Application.Standards;

namespace RATools.Tests.Standards;

public sealed class BundledStandardsAssetResolverTests
{
    [Fact]
    public void Build_ReturnsAssetWithSha256FromBundledFile()
    {
        using var assets = new TemporaryDirectory();
        var relativePath = Path.Combine("reference", "dtd", "sample.dtd");
        var assetPath = Path.Combine(assets.Path, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(assetPath)!);
        File.WriteAllText(assetPath, "abc");
        var resolver = new BundledStandardsAssetResolver(assets.Path);

        var asset = resolver.Build(
            "sample-dtd",
            "Sample DTD",
            "DTD",
            "1.0",
            "reference/dtd/sample.dtd",
            "https://example.test/sample.dtd",
            new DateOnly(2026, 1, 2));

        Assert.Equal("sample-dtd", asset.Key);
        Assert.Equal("Sample DTD", asset.DisplayName);
        Assert.Equal("DTD", asset.Category);
        Assert.Equal("1.0", asset.Version);
        Assert.Equal("reference/dtd/sample.dtd", asset.LocalRelativePath);
        Assert.Equal("https://example.test/sample.dtd", asset.SourceUrl);
        Assert.Equal(new DateOnly(2026, 1, 2), asset.SupportedFrom);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", asset.Sha256);
    }

    [Fact]
    public void Build_ThrowsWhenBundledAssetIsMissing()
    {
        using var assets = new TemporaryDirectory();
        var resolver = new BundledStandardsAssetResolver(assets.Path);

        void Act() => resolver.Build(
            "missing-dtd",
            "Missing DTD",
            "DTD",
            "1.0",
            "reference/dtd/missing.dtd",
            "https://example.test/missing.dtd",
            supportedFrom: null);

        var exception = Assert.Throws<StandardsAssetMissingException>(Act);

        Assert.Contains("Bundled standards asset", exception.Message);
        Assert.Contains("reference/dtd/missing.dtd", exception.Message);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"ratools-standards-assets-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
