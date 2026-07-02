using System.Xml.Linq;
using RATools.Application.Publishing.EuRegional;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Standards;

namespace RATools.Tests.Publishing.EuRegional;

public sealed class EuRegionalXmlWriterTests
{
    [Fact]
    public void Write_GeneratesEmptyEuRegionalRootWithDoctypeAndNamespaces()
    {
        var writer = new EuRegionalXmlWriter();
        var package = CreatePackage(module1Leaves: []);

        var result = writer.Write(package);

        Assert.Equal("eu-regional.xml", result.FileName);
        Assert.Equal("m1/eu/eu-regional.xml", result.RelativePath);
        Assert.Equal("eu-regional", result.Document.Root?.Name.LocalName);
        Assert.Equal("http://www.ema.europa.eu/eu-ectd", result.Document.Root?.Name.NamespaceName);
        Assert.Equal("EU M1", result.Document.Root?.Attribute("dtd-version")?.Value);
        Assert.Equal("eu-regional:eu-regional", result.Document.DocumentType?.Name);
        Assert.Equal("../../util/dtd/eu-regional.dtd", result.Document.DocumentType?.SystemId);
        Assert.Contains("xmlns:eu-regional=\"http://www.ema.europa.eu/eu-ectd\"", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("xmlns:xlink=\"http://www.w3c.org/1999/xlink\"", result.XmlContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_EmitsModule1LeavesWithRelativeHrefAndMd5Checksum()
    {
        var writer = new EuRegionalXmlWriter();
        var package = CreatePackage(module1Leaves:
        [
            CreateLeaf("m1.0", "leaf-11111111111111111111111111111111", "cover.pdf", "m1/eu/10-cover/cover.pdf")
        ]);

        var result = writer.Write(package);
        var leaf = result.Document.Descendants("leaf").Single();

        Assert.Contains("<m1-eu-regional><leaf", result.XmlContent, StringComparison.Ordinal);
        Assert.Equal("leaf-11111111111111111111111111111111", leaf.Attribute("ID")?.Value);
        Assert.Equal("new", leaf.Attribute("operation")?.Value);
        Assert.Equal("md5-cover.pdf", leaf.Attribute("checksum")?.Value);
        Assert.Equal("md5", leaf.Attribute("checksum-type")?.Value);
        Assert.Equal("simple", leaf.Attribute(XName.Get("type", "http://www.w3c.org/1999/xlink"))?.Value);
        Assert.Equal("10-cover/cover.pdf", leaf.Attribute(XName.Get("href", "http://www.w3c.org/1999/xlink"))?.Value);
        Assert.Equal("cover", leaf.Element("title")?.Value);
    }

    [Fact]
    public void Write_ThrowsForNonEuProfile()
    {
        var writer = new EuRegionalXmlWriter();
        var package = CreatePackage(backboneXml: BackboneXmlProfiles.FdaEctd322UsRegional33);

        var exception = Assert.Throws<EuRegionalXmlWriterException>(() => writer.Write(package));

        Assert.Contains("EU regional", exception.Message, StringComparison.Ordinal);
    }

    private static EctdSequencePackage CreatePackage(
        BackboneXmlProfile? backboneXml = null,
        IReadOnlyCollection<EctdLeaf>? module1Leaves = null)
        => new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "EU123456",
            "0001",
            "EU eCTD v3.2.2 + EU Regional M1",
            "3.2.2",
            "EU M1",
            backboneXml ?? BackboneXmlProfiles.EuEctd322Regional,
            new EctdApplicationMetadata("EU123456", "Acme Pharma", "EU", "eu-ectd-3.2.2", "maa"),
            new EctdSequenceMetadata("0001", "initial", null, "Initial sequence", "Acme Pharma", null),
            new EctdUsRegionalMetadata(
                "EU123456",
                "Acme Pharma",
                "Initial sequence",
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                string.Empty,
                "maa",
                "initial",
                string.Empty,
                null),
            module1Leaves ?? [],
            [],
            []);

    private static EctdLeaf CreateLeaf(string ctdSection, string leafId, string fileName, string href)
        => new(
            Guid.Parse($"{leafId[5..13]}-{leafId[13..17]}-{leafId[17..21]}-{leafId[21..25]}-{leafId[25..37]}"),
            Guid.NewGuid(),
            leafId,
            "0001",
            ctdSection,
            "m1",
            "new",
            Path.GetFileNameWithoutExtension(fileName),
            href,
            fileName,
            "application/pdf",
            $"C:/workspace/0001/{fileName}",
            10,
            $"sha-{fileName}",
            $"md5-{fileName}",
            null);
}
