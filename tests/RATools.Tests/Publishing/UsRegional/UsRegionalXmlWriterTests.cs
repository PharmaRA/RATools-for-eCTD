using System.Xml.Linq;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.UsRegional;

namespace RATools.Tests.Publishing.UsRegional;

public sealed class UsRegionalXmlWriterTests
{
    [Fact]
    public void WriteResult_ExposesExpectedContract()
    {
        var document = new XDocument(new XElement("root"));

        var result = new UsRegionalXmlWriteResult(
            "us-regional.xml",
            "m1/us/us-regional.xml",
            document,
            "<root />");

        Assert.Equal("us-regional.xml", result.FileName);
        Assert.Equal("m1/us/us-regional.xml", result.RelativePath);
        Assert.Same(document, result.Document);
        Assert.Equal("<root />", result.XmlContent);
    }

    [Fact]
    public void Write_GeneratesRootDoctypeNamespacesAndAdmin()
    {
        var writer = new UsRegionalXmlWriter();
        var package = CreatePackage(module1Leaves: []);

        var result = writer.Write(package);

        Assert.Equal("us-regional.xml", result.FileName);
        Assert.Equal("m1/us/us-regional.xml", result.RelativePath);
        Assert.Equal("fda-regional", result.Document.Root?.Name.LocalName);
        Assert.Equal("http://www.ich.org/fda", result.Document.Root?.Name.NamespaceName);
        Assert.Equal("3.3", result.Document.Root?.Attribute("dtd-version")?.Value);
        Assert.Equal("fda-regional:fda-regional", result.Document.DocumentType?.Name);
        Assert.Equal("../../util/dtd/us-regional-v3-3.dtd", result.Document.DocumentType?.SystemId);
        Assert.Contains("xmlns:fda-regional=\"http://www.ich.org/fda\"", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("xmlns:xlink=\"http://www.w3c.org/1999/xlink\"", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("<admin><applicant-info>", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("<id>ANDA123456</id>", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("<company-name>Acme Pharma</company-name>", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("<submission-description>Initial sequence</submission-description>", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("<applicant-contact-name applicant-contact-type=\"regulatory\">Jane Regulatory</applicant-contact-name>", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("<telephone telephone-number-type=\"office\">301-555-0100</telephone>", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("<email>jane.regulatory@example.test</email>", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("<application application-containing-files=\"false\">", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("<application-number application-type=\"anda\">ANDA123456</application-number>", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("<submission-id submission-type=\"original-application\">0001</submission-id>", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("<sequence-number submission-sub-type=\"initial\">0001</sequence-number>", result.XmlContent, StringComparison.Ordinal);
        Assert.DoesNotContain("<m1-regional>", result.XmlContent, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ThrowsArgumentNullExceptionForNullPackage()
    {
        var writer = new UsRegionalXmlWriter();

        void Act() => writer.Write(null!);

        Assert.Throws<ArgumentNullException>(Act);
    }

    [Fact]
    public void Write_ThrowsMetadataExceptionForMissingRequiredRegionalMetadata()
    {
        var writer = new UsRegionalXmlWriter();
        var package = CreatePackage(usRegional: CreateUsRegionalMetadata(applicantContactName: ""));

        void Act() => writer.Write(package);

        var exception = Assert.Throws<UsRegionalXmlMetadataException>(Act);

        Assert.Equal(package.ApplicationId, exception.ApplicationId);
        Assert.Equal(package.SequenceNumber, exception.SequenceNumber);
        Assert.Equal("ApplicantContactName", exception.FieldName);
        Assert.Equal("is required", exception.Reason);
    }

    [Fact]
    public void Write_ThrowsSectionMappingExceptionWhenModule1LeavesExistBeforeMappingIsImplemented()
    {
        var writer = new UsRegionalXmlWriter();
        var leaf = CreateLeaf("m1.2", "leaf-11111111111111111111111111111111", "cover.pdf");
        var package = CreatePackage(module1Leaves: [leaf]);

        void Act() => writer.Write(package);

        var exception = Assert.Throws<UsRegionalXmlSectionMappingException>(Act);
        Assert.Equal(package.ApplicationId, exception.ApplicationId);
        Assert.Equal(package.SequenceNumber, exception.SequenceNumber);
        Assert.Equal(leaf.PlacementId, exception.PlacementId);
        Assert.Equal("m1.2", exception.CtdSection);
        Assert.Equal("Module 1 leaf mapping is not implemented", exception.Reason);
    }

    private static EctdSequencePackage CreatePackage(
        EctdUsRegionalMetadata? usRegional = null,
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
            new EctdSequenceMetadata("0001", "original-application", "initial", "Initial sequence", "Acme Pharma", "356h"),
            usRegional ?? CreateUsRegionalMetadata(),
            module1Leaves ?? [],
            ichLeaves ?? [],
            []);
    }

    private static EctdUsRegionalMetadata CreateUsRegionalMetadata(
        string applicantContactName = "Jane Regulatory")
    {
        return new EctdUsRegionalMetadata(
            "ANDA123456",
            "Acme Pharma",
            "Initial sequence",
            applicantContactName,
            "regulatory",
            "301-555-0100",
            "office",
            "jane.regulatory@example.test",
            "anda",
            "original-application",
            "initial",
            "356h");
    }

    private static EctdLeaf CreateLeaf(string ctdSection, string leafId, string fileName)
    {
        return new EctdLeaf(
            Guid.Parse($"{leafId[5..13]}-{leafId[13..17]}-{leafId[17..21]}-{leafId[21..25]}-{leafId[25..37]}"),
            Guid.NewGuid(),
            leafId,
            "0001",
            ctdSection,
            ctdSection.Split('.')[0],
            "new",
            Path.GetFileNameWithoutExtension(fileName),
            $"m1/us/{fileName}",
            fileName,
            "application/pdf",
            $"C:/workspace/0001/m1/us/{fileName}",
            10,
            $"sha-{fileName}",
            null);
    }
}
