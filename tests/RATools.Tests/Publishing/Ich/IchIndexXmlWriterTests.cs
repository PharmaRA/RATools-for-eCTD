using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using RATools.Application;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Standards;

namespace RATools.Tests.Publishing.Ich;

public sealed class IchIndexXmlWriterTests
{
    [Fact]
    public void WriteResult_ExposesExpectedContract()
    {
        var document = new XDocument(new XElement("root"));

        var result = new IchIndexXmlWriteResult("index.xml", document, "<root />");

        Assert.Equal("index.xml", result.FileName);
        Assert.Same(document, result.Document);
        Assert.Equal("<root />", result.XmlContent);
    }

    [Fact]
    public void Write_GeneratesEmptyIchRootWithDoctypeAndNamespaces()
    {
        var writer = new IchIndexXmlWriter();
        var package = CreatePackage(ichLeaves: []);

        var result = writer.Write(package);

        Assert.Equal("index.xml", result.FileName);
        Assert.Equal("ectd", result.Document.Root?.Name.LocalName);
        Assert.Equal("http://www.ich.org/ectd", result.Document.Root?.Name.NamespaceName);
        Assert.Equal("3.2", result.Document.Root?.Attribute("dtd-version")?.Value);
        Assert.Equal("ectd:ectd", result.Document.DocumentType?.Name);
        Assert.Equal("util/dtd/ich-ectd-3-2.dtd", result.Document.DocumentType?.SystemId);
        Assert.Contains("xmlns:ectd=\"http://www.ich.org/ectd\"", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("xmlns:xlink=\"http://www.w3c.org/1999/xlink\"", result.XmlContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_PreservesFdaIchXmlContract()
    {
        var writer = new IchIndexXmlWriter();
        var package = CreatePackage(ichLeaves: [CreateLeaf("m3.2", "leaf-99999999999999999999999999999999", "quality.pdf")]);

        var result = writer.Write(package);

        Assert.Equal("index.xml", result.FileName);
        Assert.Contains("<!DOCTYPE ectd:ectd", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("\"util/dtd/ich-ectd-3-2.dtd\"", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("xmlns:ectd=\"http://www.ich.org/ectd\"", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("dtd-version=\"3.2\"", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("checksum-type=\"md5\"", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("xlink:href=\"m3/2/quality.pdf\"", result.XmlContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ThrowsArgumentNullExceptionForNullPackage()
    {
        var writer = new IchIndexXmlWriter();

        void Act() => writer.Write(null!);

        Assert.Throws<ArgumentNullException>(Act);
    }

    [Fact]
    public void Write_MapsIchLeavesToDtdSectionElements()
    {
        var writer = new IchIndexXmlWriter();
        var package = CreatePackage(ichLeaves:
        [
            CreateLeaf("m5.3.5.1", "leaf-00000000000000000000000000000005", "clinical.pdf"),
            CreateLeaf("m3.2", "leaf-00000000000000000000000000000003", "quality.pdf"),
            CreateLeaf("m2", "leaf-00000000000000000000000000000002", "summary.pdf"),
            CreateLeaf("m4.2", "leaf-00000000000000000000000000000004", "nonclinical.pdf")
        ]);

        var result = writer.Write(package);
        var xml = result.XmlContent;

        Assert.Contains("<m2-common-technical-document-summaries>", xml, StringComparison.Ordinal);
        Assert.Contains("<m3-quality><m3-2-body-of-data>", xml, StringComparison.Ordinal);
        Assert.Contains("<m4-nonclinical-study-reports><m4-2-study-reports>", xml, StringComparison.Ordinal);
        Assert.Contains("<m5-clinical-study-reports><m5-3-clinical-study-reports><m5-3-5-reports-of-efficacy-and-safety-studies><m5-3-5-1-study-reports-of-controlled-clinical-studies-pertinent-to-the-claimed-indication>", xml, StringComparison.Ordinal);
        Assert.True(xml.IndexOf("<m2-common", StringComparison.Ordinal) < xml.IndexOf("<m3-quality", StringComparison.Ordinal));
        Assert.True(xml.IndexOf("<m3-quality", StringComparison.Ordinal) < xml.IndexOf("<m4-nonclinical", StringComparison.Ordinal));
        Assert.True(xml.IndexOf("<m4-nonclinical", StringComparison.Ordinal) < xml.IndexOf("<m5-clinical", StringComparison.Ordinal));
    }

    [Fact]
    public void Write_IgnoresModule1Leaves()
    {
        var writer = new IchIndexXmlWriter();
        var module1Leaf = CreateLeaf("m1.1", "leaf-00000000000000000000000000000001", "m1.pdf");
        var package = CreatePackage(module1Leaves: [module1Leaf], ichLeaves: []);

        var result = writer.Write(package);

        Assert.DoesNotContain("m1-administrative-information-and-prescribing-information", result.XmlContent, StringComparison.Ordinal);
        Assert.DoesNotContain("leaf-00000000000000000000000000000001", result.XmlContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_EmitsLeafAttributesAndLifecycleModifiedFile()
    {
        var writer = new IchIndexXmlWriter();
        var lifecycle = new EctdLifecycleReference(Guid.NewGuid(), Guid.NewGuid(), "0000", "m3/32-body-of-data/old.pdf");
        var package = CreatePackage(ichLeaves:
        [
            CreateLeaf("m3.2", "leaf-11111111111111111111111111111111", "new.pdf", "replace", lifecycle)
        ]);

        var result = writer.Write(package);
        var leaf = result.Document.Descendants("leaf").Single();

        Assert.Equal("leaf-11111111111111111111111111111111", leaf.Attribute("ID")?.Value);
        Assert.Equal("replace", leaf.Attribute("operation")?.Value);
        Assert.Equal("md5-new.pdf", leaf.Attribute("checksum")?.Value);
        Assert.Equal("md5", leaf.Attribute("checksum-type")?.Value);
        Assert.Equal("simple", leaf.Attribute(XName.Get("type", "http://www.w3c.org/1999/xlink"))?.Value);
        Assert.Equal("m3/2/new.pdf", leaf.Attribute(XName.Get("href", "http://www.w3c.org/1999/xlink"))?.Value);
        Assert.Equal("m3/32-body-of-data/old.pdf", leaf.Attribute("modified-file")?.Value);
        Assert.Equal("new", leaf.Element("title")?.Value);
    }

    [Fact]
    public void Write_DoesNotEmitPrototypeOnlyLeafChildren()
    {
        var writer = new IchIndexXmlWriter();
        var package = CreatePackage(ichLeaves: [CreateLeaf("m3.2", "leaf-22222222222222222222222222222222", "quality.pdf")]);

        var result = writer.Write(package);

        Assert.DoesNotContain("<fileName>", result.XmlContent, StringComparison.Ordinal);
        Assert.DoesNotContain("<mimeType>", result.XmlContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ProducesStableXmlForRepeatedWrites()
    {
        var writer = new IchIndexXmlWriter();
        var package = CreatePackage(ichLeaves:
        [
            CreateLeaf("m3.2", "leaf-33333333333333333333333333333333", "quality-a.pdf"),
            CreateLeaf("m3.2", "leaf-33333333333333333333333333333334", "quality-b.pdf")
        ]);

        var first = writer.Write(package).XmlContent;
        var second = writer.Write(package).XmlContent;

        Assert.Equal(first, second);
    }

    [Fact]
    public void Write_ThrowsForUnknownIchSection()
    {
        var writer = new IchIndexXmlWriter();
        var package = CreatePackage(ichLeaves: [CreateLeaf("m3.999", "leaf-44444444444444444444444444444444", "bad.pdf")]);

        var exception = Assert.Throws<IchIndexXmlSectionMappingException>(() => writer.Write(package));

        Assert.Equal(package.ApplicationId, exception.ApplicationId);
        Assert.Equal(package.SequenceNumber, exception.SequenceNumber);
        Assert.Equal("m3.999", exception.CtdSection);
        Assert.Equal("section is not in the supported ICH profile", exception.Reason);
    }

    [Fact]
    public void AddApplication_RegistersIchIndexXmlWriter()
    {
        var services = new ServiceCollection();

        services.AddApplication();

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(IIchIndexXmlWriter));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(IchIndexXmlWriter), descriptor.ImplementationType);
    }

    private static EctdLeaf CreateLeaf(string ctdSection, string leafId, string fileName, string operation = "new", EctdLifecycleReference? lifecycle = null)
    {
        return new EctdLeaf(
            Guid.Parse($"{leafId[5..13]}-{leafId[13..17]}-{leafId[17..21]}-{leafId[21..25]}-{leafId[25..37]}"),
            Guid.NewGuid(),
            leafId,
            "0001",
            ctdSection,
            ctdSection.Split('.')[0],
            operation,
            Path.GetFileNameWithoutExtension(fileName),
            $"{ctdSection.Replace('.', '/')}/{fileName}",
            fileName,
            "application/pdf",
            $"C:/workspace/0001/{ctdSection}/{fileName}",
            10,
            $"sha-{fileName}",
            $"md5-{fileName}",
            lifecycle);
    }

    private static EctdSequencePackage CreatePackage(
        IReadOnlyCollection<EctdLeaf>? module1Leaves = null,
        IReadOnlyCollection<EctdLeaf>? ichLeaves = null)
    {
        return new EctdSequencePackage(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            "ANDA123456",
            "0001",
            "FDA CDER/CBER eCTD v3.2.2 + US Regional M1 v3.3",
            "3.2.2",
            "3.3",
            BackboneXmlProfiles.FdaEctd322UsRegional33,
            new EctdApplicationMetadata("ANDA123456", "Acme Pharma", "US", "us-fda-ectd-322", "anda"),
            new EctdSequenceMetadata("0001", "original-application", null, "Initial sequence", "Acme Pharma", "356h"),
            new EctdUsRegionalMetadata(
                "ANDA123456",
                "Acme Pharma",
                "Initial sequence",
                "Jane Regulatory",
                "regulatory",
                "301-555-0100",
                "office",
                "jane.regulatory@example.test",
                "anda",
                "original-application",
                "initial",
                "356h"),
            module1Leaves ?? [],
            ichLeaves ?? [],
            []);
    }
}
