using System.Xml.Linq;
using RATools.Application.Publishing.EuRegional;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Standards;
using RATools.Application.Publishing.Validation;
using RATools.Application.Abstractions.Publishing;
using RATools.Domain.RegionalMetadata;

namespace RATools.Tests.Publishing.EuRegional;

public sealed class ScopedEuRegionalWriterTests
{
    private static readonly string[] ExpectedValues1 = new[] { "de", "fr" };
    private static readonly string[] ExpectedValues2 = new[] { "de", "fr" };
    private static readonly string[] ExpectedValues3 = new[] { "de", "fr" };
    private static readonly string[] ExpectedValues4 = new[] { "spc", "pl" };
    private static readonly string[] ExpectedValues5 = new[] { "Applicant DE", "Applicant FR" };
    private static readonly string[] ExpectedValues6 = new[] { "business-de", "business-fr" };
    private static readonly string[] ExpectedValues7 = new[] { "DE-BFARM", "FR-ANSM" };
    private static readonly string[] ExpectedValues8 = new[] { "track-first", "track-first", "track-third" };
    private static readonly string[] ExpectedValues9 = new[] { "name-first", "name-second" };
    private static readonly string[] ExpectedValues10 = new[] { "inn-first", "inn-second" };
    private static readonly string[] ExpectedValues11 = new[] { "0001", "0001" };
    private static readonly string[] ExpectedValues12 = new[] { "de", "fr", "de" };

    [Fact]
    public void OrderedEnvelopesPreserveEveryExplicitValueAndActualSequence()
    {
        var firstEnvelope = Envelope("de") with { RelatedSequences = [new(Guid.NewGuid(), "0001"), new(Guid.NewGuid(), "0001")] };
        var xml = new EuRegionalXmlWriter().Write(Package(content: new(1, "EU", Eu: new([firstEnvelope, Envelope("fr")])))).Document;
        var envelopes = xml.Descendants("envelope").ToArray();
        Assert.Equal(ExpectedValues1, envelopes.Select(item => item.Attribute("country")!.Value));
        Assert.Equal(ExpectedValues5, envelopes.Select(item => item.Element("applicant")!.Value));
        Assert.Equal(ExpectedValues6, envelopes.Select(item => item.Element("identifier")!.Value));
        Assert.Equal(ExpectedValues7, envelopes.Select(item => item.Element("agency")!.Attribute("code")!.Value));
        Assert.All(envelopes, item => Assert.Equal("national", item.Element("procedure")!.Attribute("type")!.Value));
        Assert.All(envelopes, item => Assert.Equal("0001", item.Element("sequence")!.Value));
        Assert.Equal(ExpectedValues8, envelopes[0].Descendants("procedure-tracking").Elements("number").Select(item => item.Value));
        Assert.Equal(ExpectedValues9, envelopes[0].Elements("invented-name").Select(item => item.Value));
        Assert.Equal(ExpectedValues10, envelopes[0].Elements("inn").Select(item => item.Value));
        Assert.Equal("grouping", envelopes[0].Element("submission")!.Attribute("mode")!.Value);
        Assert.Equal("submission-explicit", envelopes[0].Element("submission")!.Element("number")!.Value);
        Assert.Equal("Explicit DE description", envelopes[0].Element("submission-description")!.Value);
        Assert.Equal(ExpectedValues11, envelopes[0].Elements("related-sequence").Select(item => item.Value));
        Assert.Equal("be", xml.Descendants("m1-0-cover").Elements("specific").Single().Attribute("country")!.Value);
    }

    [Fact]
    public void EachPlacementHasItsOwnPiWrapperAndSourceOrderIsPreserved()
    {
        var first = Leaf(9, "m1.3.1"); var second = Leaf(1, "m1.3.1") with { DocumentId = first.DocumentId, Href = first.Href };
        var package = Package([first, second], documents: [new(first.PlacementId, null, "de", "de", "spc", null), new(second.PlacementId, null, "fr", "fr", "pl", null)]);
        var xml = new EuRegionalXmlWriter().Write(package).Document;
        var wrappers = xml.Descendants("pi-doc").ToArray();
        Assert.Equal(ExpectedValues2, wrappers.Select(item => item.Attribute("country")!.Value));
        Assert.Equal(ExpectedValues3, wrappers.Select(item => item.Attribute(XNamespace.Xml + "lang")!.Value));
        Assert.Equal(ExpectedValues4, wrappers.Select(item => item.Attribute("type")!.Value));
        var piLeaves = wrappers.Elements("leaf").ToArray();
        Assert.Equal(new[] { first.LeafId, second.LeafId }, piLeaves.Select(item => item.Attribute("ID")!.Value));
        Assert.Equal(2, piLeaves.Length);
        Assert.Equal(piLeaves[0].Attribute(XName.Get("href", "http://www.w3c.org/1999/xlink"))!.Value,
            piLeaves[1].Attribute(XName.Get("href", "http://www.w3c.org/1999/xlink"))!.Value);
    }

    [Fact]
    public void CountryWrapperRunsPreserveInterleavedLeafOrderAndLifecycle()
    {
        var leaves = new[] { Leaf(9, "m1.2"), Leaf(1, "m1.2"), Leaf(5, "m1.2") };
        leaves[1] = leaves[1] with { Operation = "replace", Lifecycle = new(Guid.NewGuid(), Guid.NewGuid(), "0000", "m1/eu/old.pdf", "historical-id") };
        var package = Package(leaves, documents: leaves.Select((leaf, index) => new RegionalDocumentContext(leaf.PlacementId, null, index == 1 ? "fr" : "de", "en", null, null)).ToArray());
        var xml = new EuRegionalXmlWriter().Write(package).Document;
        var wrappers = xml.Descendants("m1-2-form").Elements("specific").ToArray();
        Assert.Equal(ExpectedValues12, wrappers.Select(item => item.Attribute("country")!.Value));
        var formLeaves = wrappers.Elements("leaf").ToArray();
        Assert.Equal(leaves.Select(item => item.LeafId), formLeaves.Select(item => item.Attribute("ID")!.Value));
        Assert.All(formLeaves, item => Assert.Equal("en", item.Attribute(XNamespace.Xml + "lang")!.Value));
        Assert.EndsWith("#historical-id", formLeaves[1].Attribute("modified-file")!.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactNodeContextAppliesOnlyWhenDocumentContextIsAbsent()
    {
        var node = Guid.NewGuid(); var leaf = Leaf(1, "m1.3.1") with { NodeInstanceId = node };
        var package = Package([leaf], nodes: [new(node, "de", "de", "spc", null)]);
        var wrapper = new EuRegionalXmlWriter().Write(package).Document.Descendants("pi-doc").Single();
        Assert.Equal("de", wrapper.Attribute("country")!.Value);
        var cleared = Package([leaf], nodes: [new(node, "de", "de", "spc", null)], documents: [new(leaf.PlacementId, node, null, null, null, null)]);
        var error = Assert.Throws<EuRegionalXmlWriterException>(() => new EuRegionalXmlWriter().Write(cleared));
        Assert.Contains("documentContexts[0].country", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RealCoverCountryIsIndependentOfEnvelopeCountry()
    {
        var cover = Leaf(1, "m1.0");
        var xml = new EuRegionalXmlWriter().Write(Package([cover], documents: [new(cover.PlacementId, null, "fr", null, null, null)], emptyCover: false)).Document;
        Assert.Equal("fr", xml.Descendants("m1-0-cover").Elements("specific").Single().Attribute("country")!.Value);
    }

    [Fact]
    public void UppercaseExactSectionsRetainCoverAndPiContext()
    {
        var cover = Leaf(1, "M1.0"); var pi = Leaf(2, "M1.3.1");
        var xml = new EuRegionalXmlWriter().Write(Package([cover, pi], emptyCover: false,
            documents: [new(cover.PlacementId, null, "be", null, null, null), new(pi.PlacementId, null, "fr", "fr", "pl", null)])).Document;
        Assert.Equal("be", xml.Descendants("m1-0-cover").Elements("specific").Single().Attribute("country")!.Value);
        Assert.Equal("fr", xml.Descendants("pi-doc").Single().Attribute("country")!.Value);
        Assert.Equal(2, xml.Descendants("leaf").Count());
    }

    [Fact]
    public void MissingCoverCountryCannotBeInferredFromEnvelopes()
    {
        var package = Package(emptyCover: false);
        var error = Assert.Throws<EuRegionalXmlWriterException>(() => new EuRegionalXmlWriter().Write(package));
        Assert.Contains("m1.0.country", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MissingTypedEnvelopeCannotFallBackToExplicitLegacyObject()
    {
        var package = Package(content: new(1, "EU", Eu: new([]))) with { EuRegional = Legacy() };
        var error = Assert.Throws<EuRegionalXmlWriterException>(() => new EuRegionalXmlWriter().Write(package));
        Assert.Contains("eu.envelopes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AbsentRegionalMetadataRequiresExplicitLegacyObject()
    {
        var error = Assert.Throws<EuRegionalXmlWriterException>(() => new EuRegionalXmlWriter().Write(Package() with { RegionalMetadata = null }));
        Assert.Contains("regionalMetadata.eu", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("identifier")]
    [InlineData("applicant")]
    public void RequiredEnvelopeFieldsReportPrecisePaths(string field)
    {
        var envelope = Envelope("de");
        envelope = field == "identifier" ? envelope with { Identifier = null } : envelope with { Applicant = null };
        var error = Assert.Throws<EuRegionalXmlWriterException>(() => new EuRegionalXmlWriter().Write(Package(content: new(1, "EU", Eu: new([envelope])))));
        Assert.Contains("eu.envelopes[0]." + field, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("country")]
    [InlineData("language")]
    [InlineData("productInformationType")]
    public void MissingPiContextFieldsCannotInheritGlobalDefaults(string field)
    {
        var leaf = Leaf(1, "m1.3.1");
        var context = new RegionalDocumentContext(leaf.PlacementId, null, field == "country" ? null : "de", field == "language" ? null : "de", field == "productInformationType" ? null : "spc", null);
        var error = Assert.Throws<EuRegionalXmlWriterException>(() => new EuRegionalXmlWriter().Write(Package([leaf], documents: [context])));
        Assert.Contains("documentContexts[0]." + field, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("US")]
    [InlineData("foreign-application")]
    [InlineData("foreign-sequence")]
    [InlineData("future-workspace")]
    public void TypedMetadataMustMatchExactPackageScope(string mismatch)
    {
        var package = Package();
        var old = package.RegionalMetadata!;
        var revision = RegionalMetadataRevision.Create(Guid.NewGuid(), mismatch == "foreign-application" ? Guid.NewGuid() : package.ApplicationId,
            mismatch == "foreign-sequence" ? "0002" : "0001", 1, mismatch == "future-workspace" ? 3 : 2, old.StandardsSnapshotId,
            mismatch == "US" ? new(1, "US", Us: new(null, null, null, [], null, null, null, null)) : old.Content,
            null, old.NodeContexts, [], null, null, [], [], null, null, DateTime.UtcNow);
        var error = Assert.Throws<EuRegionalXmlWriterException>(() => new EuRegionalXmlWriter().Write(package with { RegionalMetadata = revision }));
        Assert.Contains("regionalMetadata", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FdaFormFieldAndWrongPlacementNodeAreRejectedPrecisely()
    {
        var leaf = Leaf(1, "m1.2");
        var error = Assert.Throws<EuRegionalXmlWriterException>(() => new EuRegionalXmlWriter().Write(Package([leaf], documents: [new(leaf.PlacementId, null, "de", null, null, "fda-form")])));
        Assert.Contains("documentContexts[0].formType", error.Message, StringComparison.Ordinal);
        error = Assert.Throws<EuRegionalXmlWriterException>(() => new EuRegionalXmlWriter().Write(Package([leaf], documents: [new(leaf.PlacementId, Guid.NewGuid(), "de", null, null, null)])));
        Assert.Contains("documentContexts[0].nodeInstanceId", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ema", "centralised")]
    [InlineData("at", "national")]
    [InlineData("de", "decentralised")]
    public void TrialWriterRejectsDtdCodesOutsideExplicitNationalDeFrBeScope(string country, string procedure)
    {
        var error = Assert.Throws<EuRegionalXmlWriterException>(() => new EuRegionalXmlWriter().Write(Package(content: new(1, "EU", Eu: new([Envelope(country) with { Procedure = procedure }])))));
        Assert.Contains("eu.envelopes[0]", error.Message, StringComparison.Ordinal);
        Assert.Contains("trial", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompleteExplicitTypedOutputValidatesAgainstOfficialDtd()
    {
        var leaf = Leaf(1, "m1.3.1");
        var result = new EuRegionalXmlWriter().Write(Package([leaf], documents: [new(leaf.PlacementId, null, "fr", "fr", "pl", null)]));
        new EctdXmlValidator().Validate(new BackboneGeneratedFile(result.RelativePath, result.XmlContent), new EuEctd322StandardsProfileProvider().GetProfile("eu-ectd-3.2.2"));
    }

    internal static EctdSequencePackage Package(IReadOnlyCollection<EctdLeaf>? leaves = null, RegionalMetadataContent? content = null,
        IReadOnlyList<RegionalNodeContext>? nodes = null, IReadOnlyList<RegionalDocumentContext>? documents = null, bool emptyCover = true)
    {
        var app = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var cover = Leaf(99, "m1.0");
        var actualLeaves = (emptyCover ? new[] { cover } : []).Concat(leaves ?? []).ToArray();
        var contexts = (documents ?? []).Concat(emptyCover ? new[] { new RegionalDocumentContext(cover.PlacementId, null, "be", null, null, null) } : []).ToArray();
        var revision = RegionalMetadataRevision.Create(Guid.NewGuid(), app, "0001", 1, 2, "sha256:" + new string('a', 64),
            content ?? new(1, "EU", Eu: new([Envelope("de"), Envelope("fr")])), null, nodes ?? [], contexts, null, null, [], [], null, null, DateTime.UtcNow);
        return new(app, "folder-must-not-be-used", "0001", "EU explicit", "3.2.2", "3.1.1", BackboneXmlProfiles.EuEctd322Regional,
            new("folder-must-not-be-used", "shared-sponsor-must-not-be-used", "EU", "eu-ectd-3.2.2", null),
            new("0001", "shared-type", null, "shared-description", "shared-applicant", null),
            new("", "", "", "", "", "", "", "", "", "", "", null), actualLeaves, [], [],
            WorkspaceRevision: 2, RegionalMetadata: revision);
    }

    internal static EuEnvelope Envelope(string country) => new(Guid.NewGuid(), "business-" + country, country,
        country == "fr" ? "FR-ANSM" : country == "be" ? "BE-FAMHP" : "DE-BFARM", "national", "maa", "grouping", "submission-explicit", "initial", "Applicant " + country.ToUpperInvariant(),
        [new(Guid.NewGuid(), "track-first"), new(Guid.NewGuid(), "track-first"), new(Guid.NewGuid(), "track-third")],
        [new(Guid.NewGuid(), "name-first"), new(Guid.NewGuid(), "name-second")], [new(Guid.NewGuid(), "inn-first"), new(Guid.NewGuid(), "inn-second")],
        [new(Guid.NewGuid(), "0001")], "Explicit " + country.ToUpperInvariant() + " description");

    internal static EctdLeaf Leaf(int ordinal, string section) => new(new Guid(ordinal, 0, 0, new byte[8]), Guid.NewGuid(), "leaf-" + ordinal, "0001", section, "m1", "new", "Explicit leaf " + ordinal,
        "m1/eu/document-" + ordinal + ".pdf", "document-" + ordinal + ".pdf", "application/pdf", "C:/fixture/document.pdf", 10, new string('a', 64), new string('b', 32), null);

    private static EctdEuRegionalMetadata Legacy() => new("legacy-business-id", "ema", "maa", null, null, ["legacy-track"], "initial", "legacy-applicant", "EU-EMA", "centralised", ["legacy-name"], [], "0001", ["0001"], "legacy-description", "ema", "en", "combined");
}
