using System.Text;
using RATools.Application.Applications;

namespace RATools.Tests.Applications;

public sealed class ImportFileHashCacheTests
{
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
