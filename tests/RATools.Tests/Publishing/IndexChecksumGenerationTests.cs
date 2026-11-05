using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Publishing;
using RATools.Infrastructure.Publishing;

namespace RATools.Tests.Publishing;

public sealed class IndexChecksumGenerationTests
{
    [Fact]
    public async Task PublishedDirectoryAndZipContainOnlyTheExactIndexBytesMd5()
    {
        var root = Path.GetFullPath(Path.Combine(".artifacts/p509-index-md5", Guid.NewGuid().ToString("N")));
        var writer = new LocalBackboneFileWriter(Options.Create(new BackboneOutputOptions
        {
            RootPath = root,
            RetainJobRuns = 0
        }), NullLogger<LocalBackboneFileWriter>.Instance);
        const string index = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><index>临床总览</index>\n";
        var output = await writer.SaveAsync(Guid.NewGuid(), "0000", Guid.NewGuid(),
            [new BackboneGeneratedFile("index.xml", index)], "report.json", "0000.zip", [], []);
        var indexBytes = await File.ReadAllBytesAsync(output.FilePath);
        Assert.Equal(Encoding.UTF8.GetBytes(index), indexBytes);
        var expected = Convert.ToHexString(MD5.HashData(indexBytes)).ToLowerInvariant() + Environment.NewLine;
        var checksum = await File.ReadAllBytesAsync(Path.Combine(Path.GetDirectoryName(output.FilePath)!, "index-md5.txt"));
        Assert.Equal(Encoding.ASCII.GetBytes(expected), checksum);
        using var zip = ZipFile.OpenRead(output.PackagePath);
        using var entry = zip.GetEntry("index-md5.txt")!.Open();
        using var retained = new MemoryStream();
        await entry.CopyToAsync(retained);
        Assert.Equal(checksum, retained.ToArray());
    }
}
