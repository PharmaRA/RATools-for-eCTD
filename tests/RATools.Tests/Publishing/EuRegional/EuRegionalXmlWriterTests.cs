using System.Globalization;
using System.Xml.Linq;
using RATools.Application.Abstractions.Publishing;
using RATools.Application.Publishing.EuRegional;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Standards;
using RATools.Application.Publishing.Validation;

namespace RATools.Tests.Publishing.EuRegional;

public sealed class EuRegionalXmlWriterTests
{
    [Fact]
    public void Write_GeneratesOfficialEuBackboneEnvelopeWithDoctypeAndNamespaces()
    {
        var result = new EuRegionalXmlWriter().Write(CreatePackage(module1Leaves: []));

        Assert.Equal("eu-regional.xml", result.FileName);
        Assert.Equal("m1/eu/eu-regional.xml", result.RelativePath);
        Assert.Equal("eu-backbone", result.Document.Root?.Name.LocalName);
        Assert.Equal("http://europa.eu.int", result.Document.Root?.Name.NamespaceName);
        Assert.Equal("3.1", result.Document.Root?.Attribute("dtd-version")?.Value);
        Assert.Equal("eu:eu-backbone", result.Document.DocumentType?.Name);
        Assert.Equal("../../util/dtd/eu-regional.dtd", result.Document.DocumentType?.SystemId);
        Assert.Contains("xmlns:eu=\"http://europa.eu.int\"", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("xmlns:xlink=\"http://www.w3c.org/1999/xlink\"", result.XmlContent, StringComparison.Ordinal);
        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", result.Document.Descendants("identifier").Single().Value);
        Assert.Equal("ema", result.Document.Descendants("envelope").Single().Attribute("country")?.Value);
        Assert.Equal("maa", result.Document.Descendants("submission").Single().Attribute("type")?.Value);
        Assert.Equal("initial", result.Document.Descendants("submission-unit").Single().Attribute("type")?.Value);
        Assert.NotNull(result.Document.Descendants("m1-0-cover").Single().Element("specific"));
    }

    [Fact]
    public void Write_EmitsModuleOneLeavesWithRelativeHrefAndMd5Checksum()
    {
        var package = CreatePackage(module1Leaves:
        [
            CreateLeaf("m1.0", "leaf-11111111111111111111111111111111", "cover.pdf", "m1/eu/10-cover/cover.pdf")
        ]);

        var result = new EuRegionalXmlWriter().Write(package);
        var leaf = result.Document.Descendants("leaf").Single();

        Assert.Contains("<m1-eu><m1-0-cover><specific country=\"ema\"><leaf", result.XmlContent, StringComparison.Ordinal);
        Assert.Equal("leaf-11111111111111111111111111111111", leaf.Attribute("ID")?.Value);
        Assert.Equal("new", leaf.Attribute("operation")?.Value);
        Assert.Equal("md5-cover.pdf", leaf.Attribute("checksum")?.Value);
        Assert.Equal("md5", leaf.Attribute("checksum-type")?.Value);
        Assert.Equal("simple", leaf.Attribute(XName.Get("type", "http://www.w3c.org/1999/xlink"))?.Value);
        Assert.Equal("10-cover/cover.pdf", leaf.Attribute(XName.Get("href", "http://www.w3c.org/1999/xlink"))?.Value);
        Assert.Equal("cover", leaf.Element("title")?.Value);
    }

    [Fact]
    public void Write_EmitsNestedProductInformationMetadataAndLifecycleReference()
    {
        var lifecycle = new EctdLifecycleReference(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "0000",
            "m1/eu/13-pi/131-spclabelpl/ema/en/ema-combined.pdf");
        var leaf = CreateLeaf(
            "m1.3.1",
            "leaf-22222222222222222222222222222222",
            "ema-combined.pdf",
            "m1/eu/13-pi/131-spclabelpl/ema/en/ema-combined.pdf") with
        {
            Operation = "replace",
            Lifecycle = lifecycle
        };

        var result = new EuRegionalXmlWriter().Write(CreatePackage(module1Leaves: [leaf]));
        var piDoc = result.Document.Descendants("pi-doc").Single();
        var emittedLeaf = piDoc.Element("leaf");

        Assert.Equal("en", piDoc.Attribute(XNamespace.Xml + "lang")?.Value);
        Assert.Equal("combined", piDoc.Attribute("type")?.Value);
        Assert.Equal("ema", piDoc.Attribute("country")?.Value);
        Assert.Equal("13-pi/131-spclabelpl/ema/en/ema-combined.pdf", emittedLeaf?.Attribute(XName.Get("href", "http://www.w3c.org/1999/xlink"))?.Value);
        Assert.Equal($"../../../0000/m1/eu/eu-regional.xml#leaf-{lifecycle.TargetPlacementId:N}", emittedLeaf?.Attribute("modified-file")?.Value);
    }

    [Fact]
    public void Write_RejectsMutuallyExclusiveEnvironmentalSections()
    {
        var package = CreatePackage(module1Leaves:
        [
            CreateLeaf("m1.6.1", "leaf-33333333333333333333333333333333", "nongmo.pdf", "m1/eu/16-environrisk/161-nongmo/nongmo.pdf"),
            CreateLeaf("m1.6.2", "leaf-44444444444444444444444444444444", "gmo.pdf", "m1/eu/16-environrisk/162-gmo/gmo.pdf")
        ]);

        var exception = Assert.Throws<EuRegionalXmlWriterException>(() => new EuRegionalXmlWriter().Write(package));

        Assert.Contains("mutually exclusive", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_EmitsEveryOfficialDirectLeafSectionInDtdOrder()
    {
        var sectionPaths = new[]
        {
            "m1.0", "m1.2", "m1.3.1", "m1.3.2", "m1.3.3", "m1.3.4", "m1.3.5", "m1.3.6",
            "m1.4.1", "m1.4.2", "m1.4.3", "m1.5.1", "m1.5.2", "m1.5.3", "m1.5.4", "m1.5.5",
            "m1.6.1", "m1.7.1", "m1.8.1", "m1.9", "m1.10", "m1.responses", "m1.additional-data"
        };
        var leaves = sectionPaths
            .Select((section, index) => CreateLeaf(
                section,
                $"leaf-{(index + 10).ToString("x32", CultureInfo.InvariantCulture)}",
                $"{section.Replace('.', '-')}.pdf",
                $"m1/eu/{section.Replace('.', '-')}/document.pdf"))
            .ToArray();

        var result = new EuRegionalXmlWriter().Write(CreatePackage(module1Leaves: leaves));
        var document = result.Document;

        Assert.Equal(sectionPaths.Length, document.Descendants("leaf").Count());
        Assert.Equal(sectionPaths.Length, document.Descendants("title").Count());
        Assert.Equal("m1-0-cover", document.Descendants("m1-eu").Elements().First().Name.LocalName);
        Assert.Equal("m1-additional-data", document.Descendants("m1-eu").Elements().Last().Name.LocalName);

        var profile = new EuEctd322StandardsProfileProvider().GetProfile("eu-ectd-3.2.2");
        new EctdXmlValidator().Validate(new BackboneGeneratedFile(result.RelativePath, result.XmlContent), profile);
    }

    [Fact]
    public void Write_ThrowsForNonEuProfile()
    {
        var package = CreatePackage(backboneXml: BackboneXmlProfiles.FdaEctd322UsRegional33);

        var exception = Assert.Throws<EuRegionalXmlWriterException>(() => new EuRegionalXmlWriter().Write(package));

        Assert.Contains("EU regional", exception.Message, StringComparison.Ordinal);
    }

    private static EctdSequencePackage CreatePackage(
        BackboneXmlProfile? backboneXml = null,
        IReadOnlyCollection<EctdLeaf>? module1Leaves = null)
        => new(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "EU123456",
            "0001",
            "EU eCTD v3.2.2 + EU M1 v3.1.1",
            "3.2.2",
            "3.1.1",
            backboneXml ?? BackboneXmlProfiles.EuEctd322Regional,
            new EctdApplicationMetadata("EU123456", "Acme Pharma", "EU", "eu-ectd-3.2.2", "maa"),
            new EctdSequenceMetadata("0001", "maa", "initial", "Initial sequence", "Acme Pharma", null),
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
            [],
            new EctdEuRegionalMetadata(
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
                "ema",
                "maa",
                null,
                null,
                ["EMA/H/C/000123"],
                "initial",
                "Acme Pharma",
                "EU-EMA",
                "centralised",
                ["Wonderpill"],
                ["Example substance"],
                "0001",
                ["0001"],
                "Initial sequence",
                "ema",
                "en",
                "combined"));

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
