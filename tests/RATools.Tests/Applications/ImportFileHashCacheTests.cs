using System.Text;
using RATools.Application.Applications;

namespace RATools.Tests.Applications;

public sealed class ImportFileHashCacheTests
{
    [Fact]
    public async Task DistinctCaseSensitiveAddressesDoNotReuseTheFirstByteSourcesDigest()
    {
        var reads = new List<string>();
        var directory = Path.Combine(Path.GetTempPath(), "ratools-import-addresses");
        var lower = Path.Combine(directory, "a.pdf");
        var upper = Path.Combine(directory, "A.pdf");
        var cache = new ImportFileHashCache(path =>
        {
            reads.Add(path);
            return new MemoryStream(Encoding.UTF8.GetBytes(path == lower ? "abc" : "ABC"));
        });

        var first = await cache.GetAsync(lower);
        var second = await cache.GetAsync(upper);

        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", first.Md5);
        Assert.Equal("902fbdd2b1df0c4f70b4a5d23525e932", second.Md5);
        Assert.NotEqual(first.Sha256, second.Sha256);
        Assert.Equal(new[] { lower, upper }, reads);
    }

    [LinuxFact]
    public async Task LinuxFilesWhoseNamesDifferOnlyByCaseKeepSeparateMeasuredDigests()
    {
        var directory = Path.Combine(Path.GetTempPath(), "ratools-import-case-evidence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var lower = Path.Combine(directory, "a.pdf");
        var upper = Path.Combine(directory, "A.pdf");
        await File.WriteAllTextAsync(lower, "abc");
        await File.WriteAllTextAsync(upper, "ABC");
        Assert.Equal(2, Directory.GetFiles(directory).Length);
        var cache = new ImportFileHashCache();

        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", (await cache.GetAsync(lower)).Md5);
        Assert.Equal("902fbdd2b1df0c4f70b4a5d23525e932", (await cache.GetAsync(upper)).Md5);
        Console.WriteLine($"RETAIN Linux case-sensitive input evidence: {directory}");
    }

    [Fact]
    public async Task GetAsync_ComputesMd5AndSha256FromOneOpenPerPath()
    {
        var openCount = 0;
        var cache = new ImportFileHashCache(_ =>
        {
            openCount++;
            return new MemoryStream(Encoding.UTF8.GetBytes("abc"));
        });

        var path = Path.Combine(Path.GetTempPath(), "workspace", "0001", "m1", "leaf.txt");
        var first = await cache.GetAsync(path);
        var second = await cache.GetAsync(path);

        Assert.Equal("900150983cd24fb0d6963f7d28e17f72", first.Md5);
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", first.Sha256);
        Assert.Equal(first, second);
        Assert.Equal(1, openCount);
    }
}

internal sealed class LinuxFactAttribute : FactAttribute
{
    public LinuxFactAttribute()
    {
        if (!OperatingSystem.IsLinux()) Skip = "A real Linux filesystem is required for this acceptance case.";
    }
}
