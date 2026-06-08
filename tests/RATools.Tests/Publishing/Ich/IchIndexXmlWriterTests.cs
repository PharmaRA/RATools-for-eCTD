using System.Xml.Linq;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;

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
    public void Write_ThrowsArgumentNullExceptionForNullPackage()
    {
        var writer = new IchIndexXmlWriter();

        void Act() => writer.Write(null!);

        Assert.Throws<ArgumentNullException>(Act);
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
            new EctdApplicationMetadata("ANDA123456", "Acme Pharma", "US", "us-fda-ectd-322", "anda"),
            new EctdSequenceMetadata("0001", "original-application", null, "Initial sequence", "Acme Pharma", "356h"),
            module1Leaves ?? [],
            ichLeaves ?? [],
            []);
    }
}
