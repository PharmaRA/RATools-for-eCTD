using System.Xml.Linq;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Publishing.EuRegional;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.RegionalMetadata;
using RATools.Application.Standards;
using RATools.Domain.Applications;
using RATools.Domain.Documents;
using RATools.Domain.RegionalMetadata;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Tests.TestDoubles;

namespace RATools.Tests.Publishing.EuRegional;

public sealed class ScopedEuPackageBindingTests
{
    private static readonly string[] ExpectedValues1 = new[] { "de", "fr" };
    private static readonly string[] ExpectedValues2 = new[] { "de", "fr" };
    private static readonly string[] ExpectedImportedPiWrappersKeepSourceOrderAndRespectExplicitSortWithinEachWrapperValues3 = { "source-pi-a-next", "source-pi-a", "source-pi-b", "source-pi-b-next" };
    private static readonly string[] ExpectedEqualContextWrappersKeepOriginalSourceBoundaryEvenWhenOutputCanMergeValues4 = { "source-pi-a-next", "source-pi-a", "source-pi-b", "source-pi-b-next" };
    private static readonly string[] ExpectedImportedPiWrappersKeepSourceOrderAndRespectExplicitSortWithinEachWrapperValues5 = { "source-pi-a", "source-pi-a-next", "source-pi-b", "source-pi-b-next" };
    private static readonly string[] ExpectedEqualContextWrappersKeepOriginalSourceBoundaryEvenWhenOutputCanMergeValues6 = { "source-pi-a", "source-pi-a-next", "source-pi-b", "source-pi-b-next" };

    [Fact]
    public async Task SharedOnlyPackageDoesNotMaterializeAnyEuDefaults()
    {
        var fixture = await CreateAsync(noHead: true);
        var package = await fixture.Builder.BuildAsync(new(fixture.ApplicationId, "0001"));
        Assert.Null(package.EuRegional);
        Assert.Null(package.RegionalMetadata);
        var error = Assert.Throws<EuRegionalXmlWriterException>(() => new EuRegionalXmlWriter().Write(package));
        Assert.Contains("regionalMetadata.eu", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuilderCapturesExactHeadAndArchivedProfileAndCannotReadALaterHead()
    {
        var fixture = await CreateAsync();
        var package = await fixture.Builder.BuildAsync(new(fixture.ApplicationId, "0001"));
        Assert.Equal("EU archived explicit", package.StandardsProfile);
        Assert.Equal(fixture.Revision!.Id, package.RegionalMetadata!.Id);
        Assert.Equal(fixture.Revision.Digest, package.RegionalMetadata.Digest);
        Assert.Null(package.EuRegional);
        var next = RegionalMetadataRevision.Create(Guid.NewGuid(), fixture.ApplicationId, "0001", 2, 4, fixture.Revision.StandardsSnapshotId,
            fixture.Revision.Content! with { Eu = new([ScopedEuRegionalWriterTests.Envelope("fr")]) }, null, [], fixture.Revision.DocumentContexts,
            null, null, [], [], null, null, DateTime.UtcNow);
        await new InMemoryPersistenceTransaction().ExecuteAsync(ct => fixture.Metadata.AppendAsync(next, fixture.Revision.Id, ct));
        Assert.Equal(ExpectedValues1, new EuRegionalXmlWriter().Write(package).Document.Descendants("envelope").Select(item => item.Attribute("country")!.Value));
        await Assert.ThrowsAsync<RegionalMetadataScopeException>(() => fixture.Builder.BuildWithStandardsAsync(new(fixture.ApplicationId, "0001"), fixture.Profile with { DisplayName = "current-drift" }));
    }

    [Theory]
    [InlineData("missing", "eu.envelopes[0].identifier")]
    [InlineData("agency", "eu.envelopes[0].agency")]
    [InlineData("language", "documentContexts[0].language")]
    [InlineData("country", "documentContexts[0].country")]
    public async Task ExactCapturedDtdAndRequiredFieldsBlockInvalidHead(string change, string field)
    {
        var fixture = await CreateAsync(change: change);
        var error = await Assert.ThrowsAsync<EuRegionalXmlWriterException>(() => fixture.Builder.BuildAsync(new(fixture.ApplicationId, "0001")));
        Assert.Contains(field, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0000", "initial")]
    [InlineData("0001", "response")]
    [InlineData("0002", "response")]
    [InlineData("0003", "initial")]
    public async Task RelatedReferencesMustExistInThisApplicationAndFitPinnedUnitRule(string related, string unit)
    {
        var fixture = await CreateAsync(related: related, unit: unit);
        var error = await Assert.ThrowsAnyAsync<Exception>(() => fixture.Builder.BuildAsync(new(fixture.ApplicationId, "0001")));
        Assert.Contains("eu.envelopes[0].relatedSequences[0].sequenceNumber", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResponseCanExplicitlyReferenceEarlierExistingApplicationSequence()
    {
        var fixture = await CreateAsync(related: "0000", unit: "response", withEarlier: true);
        var package = await fixture.Builder.BuildAsync(new(fixture.ApplicationId, "0001"));
        Assert.Equal("0000", new EuRegionalXmlWriter().Write(package).Document.Descendants("related-sequence").First().Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ImportedPiWrappersKeepSourceOrderAndRespectExplicitSortWithinEachWrapper(bool explicitSort)
    {
        var fixture = await CreateAsync(imported: true, explicitSort: explicitSort, multiLeaf: true);
        var package = await fixture.Builder.BuildAsync(new(fixture.ApplicationId, "0001"));
        var xml = new EuRegionalXmlWriter().Write(package).Document;
        Assert.Equal(explicitSort ? ExpectedImportedPiWrappersKeepSourceOrderAndRespectExplicitSortWithinEachWrapperValues3
            : ExpectedImportedPiWrappersKeepSourceOrderAndRespectExplicitSortWithinEachWrapperValues5, xml.Descendants("pi-doc").Elements("leaf").Select(item => item.Attribute("ID")!.Value));
        Assert.Equal(ExpectedValues2, xml.Descendants("pi-doc").Select(item => item.Attribute("country")!.Value));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EqualContextWrappersKeepOriginalSourceBoundaryEvenWhenOutputCanMerge(bool explicitSort)
    {
        var fixture = await CreateAsync(imported: true, explicitSort: explicitSort, multiLeaf: true, sameContext: true);
        var package = await fixture.Builder.BuildAsync(new(fixture.ApplicationId, "0001"));
        var xml = new EuRegionalXmlWriter().Write(package).Document;
        Assert.Equal(explicitSort ? ExpectedEqualContextWrappersKeepOriginalSourceBoundaryEvenWhenOutputCanMergeValues4
            : ExpectedEqualContextWrappersKeepOriginalSourceBoundaryEvenWhenOutputCanMergeValues6, xml.Descendants("pi-doc").Elements("leaf").Select(item => item.Attribute("ID")!.Value));
        Assert.Single(xml.Descendants("pi-doc"));
    }

    [Theory]
    [InlineData("missing", "importedSource")]
    [InlineData("duplicate-path", "importedBackbones")]
    [InlineData("duplicate-leaf", "importedBackbones")]
    public async Task ImportedOrderingCannotInventBoundaryWhenArchiveEvidenceIsMissingOrAmbiguous(string archiveProblem, string field)
    {
        var fixture = await CreateAsync(imported: true, multiLeaf: true, sameContext: true, archiveProblem: archiveProblem);
        var error = await Assert.ThrowsAsync<EuRegionalXmlWriterException>(() => fixture.Builder.BuildAsync(new(fixture.ApplicationId, "0001")));
        Assert.Contains(field, error.Message, StringComparison.Ordinal);
    }

    private sealed record Fixture(Guid ApplicationId, EctdPackageModelBuilder Builder, RegionalMetadataRevision? Revision,
        InMemoryRegionalMetadataStore Metadata, StandardsProfile Profile);

    private static async Task<Fixture> CreateAsync(bool noHead = false, string? change = null, string related = "0001", string unit = "initial",
        bool withEarlier = false, bool imported = false, bool explicitSort = false, bool multiLeaf = false, bool sameContext = false, string? archiveProblem = null)
    {
        var applications = new InMemoryApplicationRepository(); var placements = new InMemoryDocumentPlacementRepository(); var documents = new InMemoryDocumentRepository();
        var revisions = new InMemoryWorkspaceRevisionStore(applications); var nodes = new InMemoryCtdNodeRepository(revisions, placements);
        var profile = new EuEctd322StandardsProfileProvider().GetProfile("eu-ectd-3.2.2") with { DisplayName = "EU archived explicit" };
        var snapshots = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new FixedProfiles(profile), new() { AssetRootPath = Directory.GetCurrentDirectory() });
        var snapshot = await snapshots.CaptureAsync("eu-ectd-3.2.2");
        var appId = Guid.NewGuid();
        var sequence = SubmissionSequence.Rehydrate("0001", "shared-submission", "shared-description", DateTime.UtcNow, workspaceRevision: 4);
        await applications.AddAsync(SubmissionApplication.Rehydrate(appId, "EU-folder-never-envelope", "EU", "shared-sponsor", DateTime.UtcNow,
            withEarlier ? [SubmissionSequence.Rehydrate("0000", "initial", "earlier", DateTime.UtcNow), sequence] : [sequence], "C:/fixture/EU-folder", "eu-ectd-3.2.2"));
        var document = SubmissionDocument.Rehydrate(Guid.NewGuid(), "shared.pdf", "application/pdf", 10, new string('a', 64), new string('b', 32), "C:/fixture/0001/m1/eu/shared.pdf", DateTime.UtcNow);
        await documents.AddAsync(document);
        var cover = DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, appId, "0001", "m1.0", DocumentPlacementOperation.New, "Cover", null, DateTime.UtcNow);
        await placements.AddAsync(cover);
        var contexts = new List<RegionalDocumentContext>();
        if (imported)
        {
            var first = DocumentPlacement.Rehydrate(Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff"), document.Id, appId, "0001", "m1.3.1", DocumentPlacementOperation.New, "First PI", null, DateTime.UtcNow,
                "source-pi-a", sortOrder: explicitSort ? 1 : 0, importedSource: new("m1/eu/eu-regional.xml", "shared.pdf", null));
            var second = DocumentPlacement.Rehydrate(Guid.Parse("00000001-0000-0000-0000-000000000000"), document.Id, appId, "0001", "m1.3.1", DocumentPlacementOperation.New, "Second PI", null, DateTime.UtcNow,
                "source-pi-b", importedSource: new("m1/eu/eu-regional.xml", "shared.pdf", null));
            await placements.AddAsync(second); await placements.AddAsync(first);
            contexts.Add(new(first.Id, null, "de", "de", "spc", null));
            if (multiLeaf)
            {
                var next = DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, appId, "0001", "m1.3.1", DocumentPlacementOperation.New, "First PI next", null, DateTime.UtcNow,
                    "source-pi-a-next", sortOrder: explicitSort ? 0 : 1, importedSource: new("m1/eu/eu-regional.xml", "shared.pdf", null));
                await placements.AddAsync(next); contexts.Add(new(next.Id, null, "de", "de", "spc", null));
            }
            contexts.Add(new(second.Id, null, sameContext ? "de" : "fr", sameContext ? "de" : "fr", sameContext ? "spc" : "pl", null));
            if (multiLeaf)
            {
                var next = DocumentPlacement.Rehydrate(Guid.NewGuid(), document.Id, appId, "0001", "m1.3.1", DocumentPlacementOperation.New, "Second PI next", null, DateTime.UtcNow,
                    "source-pi-b-next", sortOrder: 1, importedSource: new("m1/eu/eu-regional.xml", "shared.pdf", null));
                await placements.AddAsync(next); contexts.Add(new(next.Id, null, sameContext ? "de" : "fr", sameContext ? "de" : "fr", sameContext ? "spc" : "pl", null));
            }
        }
        contexts.Add(new(cover.Id, null, change == "country" ? "not-a-country" : "be", change == "language" ? "not-a-language" : null, null, null));
        var envelope = ScopedEuRegionalWriterTests.Envelope("de") with { RelatedSequences = [new(Guid.NewGuid(), related)], SubmissionUnit = unit };
        if (change == "missing") envelope = envelope with { Identifier = null };
        if (change == "agency") envelope = envelope with { Agency = "UNKNOWN-AGENCY" };
        var content = new RegionalMetadataContent(1, "EU", Eu: new([envelope, ScopedEuRegionalWriterTests.Envelope("fr")]));
        var metadata = new InMemoryRegionalMetadataStore(); RegionalMetadataRevision? revision = null;
        if (!noHead)
        {
            revision = RegionalMetadataRevision.Create(Guid.NewGuid(), appId, "0001", 1, 4, snapshot.Id, content, null, [], contexts, null, null, [], [], null, null, DateTime.UtcNow);
            await new InMemoryPersistenceTransaction().ExecuteAsync(ct => metadata.AppendAsync(revision, null, ct));
        }
        IApplicationImportStore imports = new InMemoryApplicationImportStore(applications, documents, placements, nodes);
        if (imported)
        {
            var first = new XElement("pi-doc", new XAttribute("country", "de"), new XAttribute(XNamespace.Xml + "lang", "de"), new XAttribute("type", "spc"),
                new XElement("leaf", new XAttribute("ID", "source-pi-a")));
            var second = new XElement("pi-doc", new XAttribute("country", sameContext ? "de" : "fr"), new XAttribute(XNamespace.Xml + "lang", sameContext ? "de" : "fr"), new XAttribute("type", sameContext ? "spc" : "pl"),
                new XElement("leaf", new XAttribute("ID", archiveProblem == "duplicate-leaf" ? "source-pi-a" : "source-pi-b")));
            if (multiLeaf) { first.Add(new XElement("leaf", new XAttribute("ID", "source-pi-a-next"))); second.Add(new XElement("leaf", new XAttribute("ID", "source-pi-b-next"))); }
            var xml = new XElement("eu-backbone", new XElement("m1-eu", new XElement("m1-3-pi", new XElement("m1-3-1-spc-label-pl", first, second)))).ToString();
            var archive = new ImportedBackbone("0001", "m1/eu/eu-regional.xml", xml);
            imports = new ArchivedImports(archiveProblem == "missing" ? [] : archiveProblem == "duplicate-path" ? [archive, archive] : [archive]);
        }
        var builder = new EctdPackageModelBuilder(applications, placements, documents, new FixedProfiles(profile with { DisplayName = "current-drift" }),
            PermissiveDocumentStorageBoundary.Instance, nodes, revisions, imports, regionalMetadataStore: metadata, standardsSnapshots: snapshots);
        return new(appId, builder, revision, metadata, profile);
    }

    private sealed class FixedProfiles(StandardsProfile profile) : IStandardsProfileProvider
    { public StandardsProfile GetProfile(string templateKey) => profile; }
    private sealed class ArchivedImports(IReadOnlyList<ImportedBackbone> backbones) : IApplicationImportStore
    {
        public Task SaveAsync(ApplicationImportBatch batch, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ImportedBackbone>> GetBackbonesAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) => Task.FromResult(backbones);
    }
}
