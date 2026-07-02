using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using RATools.Application;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Standards;

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
    public void Write_PreservesFdaRegionalXmlContract()
    {
        var writer = new UsRegionalXmlWriter();
        var package = CreatePackage(module1Leaves:
        [
            CreateLeaf("m1.2", "leaf-99999999999999999999999999999999", "cover-letter.pdf")
        ]);

        var result = writer.Write(package);

        Assert.Equal("m1/us/us-regional.xml", result.RelativePath);
        Assert.Contains("<!DOCTYPE fda-regional:fda-regional", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("\"../../util/dtd/us-regional-v3-3.dtd\"", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("xmlns:fda-regional=\"http://www.ich.org/fda\"", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("dtd-version=\"3.3\"", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("checksum-type=\"md5\"", result.XmlContent, StringComparison.Ordinal);
        Assert.Contains("xlink:href=\"12-cover-letters/cover-letter.pdf\"", result.XmlContent, StringComparison.Ordinal);
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
    public void Write_MapsModule1LeavesToDtdSectionsInOrder()
    {
        var writer = new UsRegionalXmlWriter();
        var package = CreatePackage(module1Leaves:
        [
            CreateLeaf("m1.16.2.1", "leaf-00000000000000000000000000000003", "rems.pdf"),
            CreateLeaf("m1.2", "leaf-00000000000000000000000000000001", "cover-letter.pdf"),
            CreateLeaf("m1.14.2.3", "leaf-00000000000000000000000000000002", "labeling.pdf")
        ]);

        var xml = writer.Write(package).XmlContent;

        Assert.Contains("<m1-regional>", xml, StringComparison.Ordinal);
        Assert.Contains("<m1-2-cover-letters>", xml, StringComparison.Ordinal);
        Assert.Contains("<m1-14-labeling><m1-14-2-final-labeling><m1-14-2-3-final-labeling-text>", xml, StringComparison.Ordinal);
        Assert.Contains("<m1-16-risk-management-plan><m1-16-2-risk-evaluation-and-mitigation-strategies-rems><m1-16-2-1-final-rems>", xml, StringComparison.Ordinal);
        Assert.True(xml.IndexOf("<m1-2-cover", StringComparison.Ordinal) < xml.IndexOf("<m1-14-labeling", StringComparison.Ordinal));
        Assert.True(xml.IndexOf("<m1-14-labeling", StringComparison.Ordinal) < xml.IndexOf("<m1-16-risk", StringComparison.Ordinal));
    }

    [Fact]
    public void Write_IgnoresIchLeaves()
    {
        var writer = new UsRegionalXmlWriter();
        var ichLeaf = CreateLeaf("m3.2", "leaf-00000000000000000000000000000004", "quality.pdf");
        var package = CreatePackage(module1Leaves: [], ichLeaves: [ichLeaf]);

        var xml = writer.Write(package).XmlContent;

        Assert.DoesNotContain("m3-quality", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("leaf-00000000000000000000000000000004", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_EmitsLeafAttributesRelativeHrefAndLifecycleModifiedFile()
    {
        var writer = new UsRegionalXmlWriter();
        var lifecycle = new EctdLifecycleReference(Guid.NewGuid(), Guid.NewGuid(), "0000", "m1/us/12-cover-letters/old.pdf");
        var package = CreatePackage(module1Leaves:
        [
            CreateLeaf("m1.2", "leaf-11111111111111111111111111111111", "new.pdf", "replace", lifecycle)
        ]);

        var result = writer.Write(package);
        var leaf = result.Document.Descendants("leaf").Single();

        Assert.Equal("leaf-11111111111111111111111111111111", leaf.Attribute("ID")?.Value);
        Assert.Equal("replace", leaf.Attribute("operation")?.Value);
        Assert.Equal("md5-new.pdf", leaf.Attribute("checksum")?.Value);
        Assert.Equal("md5", leaf.Attribute("checksum-type")?.Value);
        Assert.Equal("simple", leaf.Attribute(XName.Get("type", "http://www.w3c.org/1999/xlink"))?.Value);
        Assert.Equal("12-cover-letters/new.pdf", leaf.Attribute(XName.Get("href", "http://www.w3c.org/1999/xlink"))?.Value);
        Assert.Equal("../../../0000/m1/us/12-cover-letters/old.pdf", leaf.Attribute("modified-file")?.Value);
        Assert.Equal("new", leaf.Element("title")?.Value);
    }

    [Fact]
    public void Write_DoesNotEmitPrototypeOnlyLeafChildren()
    {
        var writer = new UsRegionalXmlWriter();
        var package = CreatePackage(module1Leaves: [CreateLeaf("m1.2", "leaf-22222222222222222222222222222222", "cover.pdf")]);

        var xml = writer.Write(package).XmlContent;

        Assert.DoesNotContain("<fileName>", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<mimeType>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void Write_ProducesStableXmlForRepeatedWrites()
    {
        var writer = new UsRegionalXmlWriter();
        var package = CreatePackage(module1Leaves:
        [
            CreateLeaf("m1.2", "leaf-33333333333333333333333333333333", "cover-a.pdf"),
            CreateLeaf("m1.2", "leaf-33333333333333333333333333333334", "cover-b.pdf")
        ]);

        var first = writer.Write(package).XmlContent;
        var second = writer.Write(package).XmlContent;

        Assert.Equal(first, second);
    }

    [Fact]
    public void Write_ThrowsForUnknownModule1Section()
    {
        var writer = new UsRegionalXmlWriter();
        var package = CreatePackage(module1Leaves: [CreateLeaf("m1.999", "leaf-44444444444444444444444444444444", "bad.pdf")]);

        void Act() => writer.Write(package);

        var exception = Assert.Throws<UsRegionalXmlSectionMappingException>(Act);

        Assert.Equal(package.ApplicationId, exception.ApplicationId);
        Assert.Equal(package.SequenceNumber, exception.SequenceNumber);
        Assert.Equal("m1.999", exception.CtdSection);
        Assert.Equal("section is not in the supported US Regional M1 profile", exception.Reason);
    }

    [Fact]
    public void Write_ThrowsForUnsupportedAttributeHeavySection()
    {
        var writer = new UsRegionalXmlWriter();
        var package = CreatePackage(module1Leaves: [CreateLeaf("m1.15.2.1.1", "leaf-55555555555555555555555555555555", "promo.pdf")]);

        void Act() => writer.Write(package);

        var exception = Assert.Throws<UsRegionalXmlSectionMappingException>(Act);

        Assert.Equal("m1.15.2.1.1", exception.CtdSection);
        Assert.Equal("section requires unsupported regional attributes", exception.Reason);
    }

    [Fact]
    public void Write_ThrowsForModule1SectionThatDoesNotDirectlyAcceptLeaves()
    {
        var writer = new UsRegionalXmlWriter();
        var package = CreatePackage(module1Leaves: [CreateLeaf("m1.14.2", "leaf-77777777777777777777777777777777", "bad.pdf")]);

        void Act() => writer.Write(package);

        var exception = Assert.Throws<UsRegionalXmlSectionMappingException>(Act);

        Assert.Equal("m1.14.2", exception.CtdSection);
        Assert.Equal("section does not directly accept leaves", exception.Reason);
    }

    [Fact]
    public void Write_EmitsM1FormsInsideAdminFormElement()
    {
        var writer = new UsRegionalXmlWriter();
        var package = CreatePackage(module1Leaves: [CreateLeaf("m1.1", "leaf-66666666666666666666666666666666", "form-356h.pdf")]);

        var xml = writer.Write(package).XmlContent;

        Assert.Contains("<form form-type=\"356h\"><leaf", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("<m1-1-forms>", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void AddApplication_RegistersUsRegionalXmlWriter()
    {
        var services = new ServiceCollection();

        services.AddApplication();

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(IUsRegionalXmlWriter));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(UsRegionalXmlWriter), descriptor.ImplementationType);
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
            BackboneXmlProfiles.FdaEctd322UsRegional33,
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

    private static EctdLeaf CreateLeaf(
        string ctdSection,
        string leafId,
        string fileName,
        string operation = "new",
        EctdLifecycleReference? lifecycle = null)
    {
        var href = ctdSection switch
        {
            "m1.2" => $"m1/us/12-cover-letters/{fileName}",
            "m1.14.2.3" => $"m1/us/114-labeling/{fileName}",
            "m1.16.2.1" => $"m1/us/116-risk-management-plan/{fileName}",
            _ => $"{ctdSection.Replace('.', '/')}/{fileName}"
        };

        return new EctdLeaf(
            Guid.Parse($"{leafId[5..13]}-{leafId[13..17]}-{leafId[17..21]}-{leafId[21..25]}-{leafId[25..37]}"),
            Guid.NewGuid(),
            leafId,
            "0001",
            ctdSection,
            ctdSection.Split('.')[0],
            operation,
            Path.GetFileNameWithoutExtension(fileName),
            href,
            fileName,
            "application/pdf",
            $"C:/workspace/0001/{ctdSection}/{fileName}",
            10,
            $"sha-{fileName}",
            $"md5-{fileName}",
            lifecycle);
    }
}
