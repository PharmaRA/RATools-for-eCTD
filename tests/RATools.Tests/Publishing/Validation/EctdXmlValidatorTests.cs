using RATools.Application.Abstractions.Publishing;
using Microsoft.Extensions.DependencyInjection;
using RATools.Application;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Publishing.Validation;
using RATools.Application.Standards;

namespace RATools.Tests.Publishing.Validation;

public sealed class EctdXmlValidatorTests
{
    [Fact]
    public void Validate_PassesForWriterGeneratedIchIndexXml()
    {
        var validator = new EctdXmlValidator();
        var package = CreatePackage();
        var result = new IchIndexXmlWriter().Write(package);

        validator.Validate(new BackboneGeneratedFile(result.FileName, result.XmlContent));
    }

    [Fact]
    public void Validate_PassesWhenDtdIsDeclaredByStandardsProfile()
    {
        var validator = new EctdXmlValidator();
        var package = CreatePackage();
        var result = new IchIndexXmlWriter().Write(package);
        var profile = new FdaEctd322StandardsProfileProvider().GetProfile("us-fda-ectd-3.2.2");

        validator.Validate(new BackboneGeneratedFile(result.FileName, result.XmlContent), profile);
    }

    [Fact]
    public void Validate_ThrowsWhenDtdIsNotDeclaredByStandardsProfile()
    {
        var validator = new EctdXmlValidator();
        var package = CreatePackage();
        var result = new IchIndexXmlWriter().Write(package);
        var profile = new StandardsProfile(
            "custom",
            "Custom",
            "Custom",
            "Custom",
            "3.2.2",
            "3.3",
            "1.0",
            "1.0",
            [],
            [],
            BackboneXmlProfiles.FdaEctd322UsRegional33);

        var exception = Assert.Throws<EctdXmlValidationException>(
            () => validator.Validate(new BackboneGeneratedFile(result.FileName, result.XmlContent), profile));

        Assert.Equal("index.xml", exception.RelativePath);
        Assert.Contains("ich-ectd-3-2.dtd", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_PassesForWriterGeneratedUsRegionalXml()
    {
        var validator = new EctdXmlValidator();
        var package = CreatePackage();
        var result = new UsRegionalXmlWriter().Write(package);

        validator.Validate(new BackboneGeneratedFile(result.RelativePath, result.XmlContent));
    }

    [Fact]
    public void Validate_ThrowsForDtdValidationError()
    {
        var validator = new EctdXmlValidator();
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE ectd:ectd SYSTEM "util/dtd/ich-ectd-3-2.dtd">
            <ectd:ectd xmlns:ectd="http://www.ich.org/ectd" xmlns:xlink="http://www.w3c.org/1999/xlink" dtd-version="3.2">
              <m2-common-technical-document-summaries>
                <leaf ID="leaf-bad" operation="new" checksum-type="md5" xlink:type="simple" xlink:href="m2/bad.pdf">
                  <title>Bad leaf</title>
                </leaf>
              </m2-common-technical-document-summaries>
            </ectd:ectd>
            """;

        var exception = Assert.Throws<EctdXmlValidationException>(
            () => validator.Validate(new BackboneGeneratedFile("index.xml", xml)));

        Assert.Equal("index.xml", exception.RelativePath);
        Assert.Contains("checksum", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_ThrowsForUnknownDtdSystemId()
    {
        var validator = new EctdXmlValidator();
        var xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <!DOCTYPE ectd:ectd SYSTEM "util/dtd/unknown.dtd">
            <ectd:ectd xmlns:ectd="http://www.ich.org/ectd" xmlns:xlink="http://www.w3c.org/1999/xlink" dtd-version="3.2" />
            """;

        var exception = Assert.Throws<EctdXmlValidationException>(
            () => validator.Validate(new BackboneGeneratedFile("index.xml", xml)));

        Assert.Equal("index.xml", exception.RelativePath);
        Assert.Contains("unknown.dtd", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddApplication_RegistersEctdXmlValidator()
    {
        var services = new ServiceCollection();

        services.AddApplication();

        var descriptor = Assert.Single(services, x => x.ServiceType == typeof(IEctdXmlValidator));
        Assert.Equal(ServiceLifetime.Singleton, descriptor.Lifetime);
        Assert.Equal(typeof(EctdXmlValidator), descriptor.ImplementationType);
    }

    private static EctdSequencePackage CreatePackage()
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
            [],
            [],
            []);
    }
}
