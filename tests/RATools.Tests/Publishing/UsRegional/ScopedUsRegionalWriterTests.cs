using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml;
using System.Xml.Linq;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Standards;
using RATools.Domain.RegionalMetadata;

namespace RATools.Tests.Publishing.UsRegional;

public sealed class ScopedUsRegionalWriterTests
{
    private static readonly string[] ExpectedValues1 = new[] { "First Contact", "Second Contact" };
    private static readonly string[] ExpectedValues2 = new[] { "office", "fax", "mobile" };
    private static readonly string[] ExpectedValues3 = new[] { "first@example.test", "alternate@example.test", "second@example.test" };
    private static readonly string[] ExpectedValues4 = new[] { "placement-form", "node-form", "sequence-form" };
    private static readonly string[] ExpectedValues5 = new[] { "leaf-1", "leaf-2", "leaf-3" };

    private static readonly Guid ApplicationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid NodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    [Fact]
    public void TypedMetadataUsesExplicitBusinessIdentifiersAndAllOrderedContacts()
    {
        var xml = new UsRegionalXmlWriter().Write(Package()).Document;
        Assert.Equal("applicant-business-id", xml.Descendants("id").Single().Value);
        Assert.Equal("999888", xml.Descendants("application-number").Single().Value);
        Assert.Equal("submission-business-id", xml.Descendants("submission-id").Single().Value);
        Assert.Equal("typed-company", xml.Descendants("company-name").Single().Value);
        Assert.Equal("typed-description", xml.Descendants("submission-description").Single().Value);
        Assert.Equal(ExpectedValues1, xml.Descendants("applicant-contact-name").Select(item => item.Value));
        Assert.Equal(ExpectedValues2, xml.Descendants("telephone").Select(item => item.Attribute("telephone-number-type")!.Value));
        Assert.Equal(ExpectedValues3, xml.Descendants("email").Select(item => item.Value));
        Assert.Equal("typed-application-type", xml.Descendants("application-number").Single().Attribute("application-type")!.Value);
        Assert.Equal("typed-submission-type", xml.Descendants("submission-id").Single().Attribute("submission-type")!.Value);
        Assert.Equal("typed-subtype", xml.Descendants("sequence-number").Single().Attribute("submission-sub-type")!.Value);
    }

    [Fact]
    public void SameFilePlacementsUseExactPlacementThenNodeThenSequenceFormsOnceInOrder()
    {
        var first = Leaf(1); var second = Leaf(2); var third = Leaf(3) with { NodeInstanceId = null };
        var package = Package([first, second, third],
            nodes: [new(NodeId, null, null, null, "node-form")],
            documents: [new(first.PlacementId, NodeId, null, null, null, "placement-form")]);
        var xml = new UsRegionalXmlWriter().Write(package).Document;
        Assert.Empty(xml.Descendants("submission-information").Elements("form"));
        var wrappers = xml.Descendants("m1-1-forms").Elements("form").ToArray();
        Assert.Equal(ExpectedValues4, wrappers.Select(item => item.Attribute("form-type")!.Value));
        Assert.Equal(ExpectedValues5, wrappers.SelectMany(item => item.Elements("leaf")).Select(item => item.Attribute("ID")!.Value));
        Assert.Single(package.PublishedFiles);
        Assert.Equal(3, xml.Descendants("leaf").Count());
        Assert.Equal("11-forms/shared.pdf", xml.Descendants("leaf").First().Attribute(XName.Get("href", "http://www.w3c.org/1999/xlink"))!.Value);
    }

    [Theory]
    [InlineData("sequence", "sequence-form")]
    [InlineData("placement", "placement-form")]
    [InlineData("node", "node-form")]
    public void UppercaseFormSectionEmitsTheExactLeafAndContextOnce(string source, string expectedForm)
    {
        var leaf = Leaf(1) with { CtdSection = "M1.1" };
        var package = Package([leaf], nodes: source == "node" ? [new(NodeId, null, null, null, "node-form")] : [],
            documents: source == "placement" ? [new(leaf.PlacementId, NodeId, null, null, null, "placement-form")] : []);
        var xml = new UsRegionalXmlWriter().Write(package).Document;
        var form = Assert.Single(xml.Descendants("m1-1-forms").Elements("form"));
        Assert.Equal(expectedForm, form.Attribute("form-type")!.Value);
        Assert.Equal("leaf-1", Assert.Single(form.Elements("leaf")).Attribute("ID")!.Value);
        Assert.Single(xml.Descendants("leaf"));
    }

    [Fact]
    public void UppercaseFormSectionWithClearedExactPlacementStillCannotInheritSequenceForm()
    {
        var leaf = Leaf(1) with { CtdSection = "M1.1" };
        var package = Package([leaf], documents: [new(leaf.PlacementId, NodeId, null, null, null, null)]);
        var error = Assert.Throws<UsRegionalXmlMetadataException>(() => new UsRegionalXmlWriter().Write(package));
        Assert.Equal("documentContexts[0].formType", error.FieldName);
    }

    [Theory]
    [InlineData("m1.2")]
    [InlineData("M1.2")]
    public void FormContextOnAnotherSectionRemainsRejected(string section)
    {
        var leaf = Leaf(1) with { CtdSection = section };
        var package = Package([leaf], documents: [new(leaf.PlacementId, NodeId, null, null, null, "wrong-form")]);
        var error = Assert.Throws<UsRegionalXmlMetadataException>(() => new UsRegionalXmlWriter().Write(package));
        Assert.Equal("documentContexts[0].formType", error.FieldName);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExplicitClearedContextDoesNotInheritAnotherForm(bool placement)
    {
        var leaf = Leaf(1);
        var package = placement
            ? Package([leaf], nodes: [new(NodeId, null, null, null, "node-form")], documents: [new(leaf.PlacementId, NodeId, null, null, null, null)])
            : Package([leaf], nodes: [new(NodeId, null, null, null, null)]);
        var error = Assert.Throws<UsRegionalXmlMetadataException>(() => new UsRegionalXmlWriter().Write(package));
        Assert.Equal(placement ? "documentContexts[0].formType" : "nodeContexts[0].formType", error.FieldName);
        Assert.Contains(leaf.PlacementId.ToString(), error.Message, StringComparison.Ordinal);
        Assert.Contains(NodeId.ToString(), error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ApplicantIdentifier", "us.applicantIdentifier")]
    [InlineData("SubmissionIdentifier", "us.submissionIdentifier")]
    [InlineData("ApplicationIdentifier", "us.applicationIdentifier")]
    [InlineData("ApplicantName", "us.applicantName")]
    [InlineData("ApplicationType", "us.applicationType")]
    public void MissingTypedFactsCannotUseCompleteLegacyMetadata(string property, string field)
    {
        var package = Package(change: revision => revision["Content"]!["Us"]![property] = null);
        var error = Assert.Throws<UsRegionalXmlMetadataException>(() => new UsRegionalXmlWriter().Write(package));
        Assert.Equal(field, error.FieldName);
    }

    [Fact]
    public void TypedRevisionMustBelongToTheExactApplicationAndSequence()
    {
        var package = Package(change: revision => revision["ApplicationId"] = Guid.NewGuid().ToString());
        Assert.ThrowsAny<Exception>(() => new UsRegionalXmlWriter().Write(package));
    }

    [Fact]
    public void WrongPlacementNodeContextCannotLeakAcrossBindings()
    {
        var leaf = Leaf(1);
        var package = Package([leaf], documents: [new(leaf.PlacementId, Guid.NewGuid(), null, null, null, "wrong-form")]);
        var error = Assert.Throws<UsRegionalXmlMetadataException>(() => new UsRegionalXmlWriter().Write(package));
        Assert.Equal("documentContexts[0].nodeInstanceId", error.FieldName);
    }

    [Fact]
    public void PromotionalAttributeBranchesRemainBlockedWithTypedMetadata()
    {
        var error = Assert.Throws<UsRegionalXmlSectionMappingException>(() => new UsRegionalXmlWriter().Write(Package([Leaf(1) with { CtdSection = "m1.15.2.1.1" }])));
        Assert.Equal("section requires unsupported regional attributes", error.Reason);
    }

    [Fact]
    public void TypedLeafKeepsExactGenericLanguageAndLifecycleAddress()
    {
        var leaf = Leaf(1) with { Operation = "replace", Lifecycle = new(Guid.NewGuid(), Guid.NewGuid(), "0000", "m1/us/11-forms/old.pdf", "previous-form", "m1/us/us-regional.xml") };
        var package = Package([leaf], nodes: [new(NodeId, null, "fr", null, "node-form")],
            documents: [new(leaf.PlacementId, NodeId, null, "en", null, "placement-form")]);
        var output = new UsRegionalXmlWriter().Write(package).Document.Descendants("leaf").Single();
        Assert.Equal("en", output.Attribute(XNamespace.Xml + "lang")!.Value);
        Assert.Equal("replace", output.Attribute("operation")!.Value);
        Assert.Equal("../../../0000/m1/us/us-regional.xml#previous-form", output.Attribute("modified-file")!.Value);
        Assert.Equal("leaf-1", output.Attribute("ID")!.Value);
    }

    [Theory]
    [InlineData("country")]
    [InlineData("productInformationType")]
    public void WrongRegionalContextIsRejectedBeforeXml(string field)
    {
        var leaf = Leaf(1);
        var context = new RegionalDocumentContext(leaf.PlacementId, NodeId, field == "country" ? "de" : null, null, field == "productInformationType" ? "spc" : null, "form");
        var error = Assert.Throws<UsRegionalXmlMetadataException>(() => new UsRegionalXmlWriter().Write(Package([leaf], documents: [context])));
        Assert.Equal("documentContexts[0]." + field, error.FieldName);
    }

    [Fact]
    public void MultipleTypedFormsValidateAgainstActualBundledDtdAndMissingAttributeFails()
    {
        var first = Leaf(1); var second = Leaf(2);
        var result = new UsRegionalXmlWriter().Write(Package([first, second], documents:
            [new(first.PlacementId, NodeId, null, null, null, "form-one"), new(second.PlacementId, NodeId, null, null, null, "form-two")]));
        Assert.Equal(2, result.Document.Descendants("m1-1-forms").Elements("form").Count());
        Assert.Empty(DtdErrors(result.XmlContent));
        var changed = XDocument.Parse(result.XmlContent);
        changed.Descendants("form").First().Attribute("form-type")!.Remove();
        Assert.NotEmpty(DtdErrors(changed.ToString(SaveOptions.DisableFormatting)));
    }

    private static List<string> DtdErrors(string xml)
    {
        var errors = new List<string>();
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Parse, ValidationType = ValidationType.DTD, XmlResolver = new BundledDtdResolver() };
        settings.ValidationEventHandler += (_, arguments) => errors.Add(arguments.Message);
        using var text = new StringReader(xml);
        using var reader = XmlReader.Create(text, settings, "https://fixture.test/0001/m1/us/us-regional.xml");
        while (reader.Read()) { }
        return errors;
    }

    private sealed class BundledDtdResolver : XmlResolver
    {
        public override ICredentials? Credentials { set { } }
        public override object GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn) =>
            absoluteUri.AbsolutePath.EndsWith("/us-regional-v3-3.dtd", StringComparison.Ordinal)
                ? new MemoryStream(File.ReadAllBytes("reference/dtd/us-regional-v3-3.dtd"))
                : throw new InvalidDataException("Unexpected external resource.");
    }

    internal static EctdSequencePackage Package(IReadOnlyList<EctdLeaf>? leaves = null, IReadOnlyList<RegionalNodeContext>? nodes = null,
        IReadOnlyList<RegionalDocumentContext>? documents = null, Action<JsonObject>? change = null)
    {
        var contacts = new RegionalContact[]
        {
            new(Guid.NewGuid(), "First Contact", "regulatory", [new(Guid.NewGuid(), "555-0101", "office"), new(Guid.NewGuid(), "555-0102", "fax")], [new(Guid.NewGuid(), "first@example.test"), new(Guid.NewGuid(), "alternate@example.test")]),
            new(Guid.NewGuid(), "Second Contact", "technical", [new(Guid.NewGuid(), "555-0103", "mobile")], [new(Guid.NewGuid(), "second@example.test")])
        };
        var revision = RegionalMetadataRevision.Create(Guid.NewGuid(), ApplicationId, "0001", 1, 4, "sha256:" + new string('a', 64),
            new(1, "US", Us: new("999888", "typed-application-type", "typed-company", contacts, "typed-submission-type", "typed-subtype", "sequence-form", "typed-description")),
            null, nodes ?? [], documents ?? [], null, null, [], [], null, null, DateTime.UtcNow);
        var regional = JsonNode.Parse(JsonSerializer.Serialize(revision))!.AsObject();
        regional["Content"]!["Us"]!["ApplicantIdentifier"] = "applicant-business-id";
        regional["Content"]!["Us"]!["SubmissionIdentifier"] = "submission-business-id";
        change?.Invoke(regional);
        // Recompute the immutable revision through its public factory when the optional fields are supported.
        var content = JsonSerializer.Deserialize<RegionalMetadataContent>(regional["Content"]!.ToJsonString())!;
        var exact = RegionalMetadataRevision.Create(revision.Id, Guid.Parse(regional["ApplicationId"]!.GetValue<string>()), "0001", 1, 4, revision.StandardsSnapshotId,
            content, null, nodes ?? [], documents ?? [], null, null, [], [], null, null, revision.CreatedUtc);
        var package = new EctdSequencePackage(ApplicationId, "ANDA-folder-value", "0001", "fixture", "3.2.2", "3.3", BackboneXmlProfiles.FdaEctd322UsRegional33,
            new("ANDA-folder-value", "legacy-sponsor", "US", "us-fda-ectd-3.2.2", "legacy-application-type"),
            new("0001", "legacy-submission-type", "legacy-subtype", "legacy-description", "legacy-applicant", "legacy-form"),
            new("legacy-id", "legacy-company", "legacy-description", "legacy-contact", "legacy-contact-type", "legacy-phone", "legacy-phone-type", "legacy@example.test", "legacy-application-type", "legacy-submission-type", "legacy-subtype", "legacy-form"),
            leaves ?? [], [], leaves is { Count: > 0 } ? [new(leaves[0].DocumentId, leaves[0].SourcePath, leaves[0].Href, leaves[0].FileName, 10, new string('a', 64), new string('b', 32))] : [], WorkspaceRevision: 4);
        var json = JsonNode.Parse(JsonSerializer.Serialize(package))!.AsObject();
        json["RegionalMetadata"] = JsonSerializer.SerializeToNode(exact);
        return JsonSerializer.Deserialize<EctdSequencePackage>(json.ToJsonString())!;
    }

    internal static EctdLeaf Leaf(int number) => new(new Guid(number, 0, 0, new byte[8]), Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"), "leaf-" + number,
        "0001", "m1.1", "m1", "new", "Form " + number, "m1/us/11-forms/shared.pdf", "shared.pdf", "application/pdf", "C:/fixture/shared.pdf", 10, new string('a', 64), new string('b', 32), null, NodeId, number);
}
