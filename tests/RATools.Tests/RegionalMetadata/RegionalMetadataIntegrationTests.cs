using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Ctd;
using RATools.Application.Finalization;
using RATools.Application.RegionalMetadata;
using RATools.Application.Standards;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.Ctd;
using RATools.Domain.Common;
using RATools.Domain.Documents;
using RATools.Domain.RegionalMetadata;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.RegionalMetadata;

// P6-03 service/model qualification. Real application/node/placement repositories
// establish scope; it does not qualify the later regional UI, import or writers.
public sealed class RegionalMetadataIntegrationTests
{
    private static readonly string[] ExpectedValues1 = new[] { "fr", "common" };
    private static readonly string[] ExpectedValues2 = new[] { "Second contact", "First contact" };
    private static readonly string[] ExpectedValues3 = new[] { "business", "fax" };
    private static readonly string[] ExpectedValues4 = new[] { "5550201", "5550202" };
    private static readonly string[] ExpectedValues5 = new[] { "second-b@example.test", "second-a@example.test" };

    [Fact]
    public async Task ExplicitEuInheritanceCopiesTheChosenDefaultsAndKeepsSequenceFieldsAfterLaterDefaultsChange()
    {
        var test = await RegionalMetadataIntegrationRig.CreateAsync();
        var first = Envelope("de", "DE-BFARM", "initial", "0001") with { Identifier = "envelope-first", SubmissionNumber = "submission-1", Description = "First sequence description" };
        var second = Envelope("fr", "FR-ANSM", "response", "0000") with { Identifier = "envelope-second", SubmissionMode = "worksharing", Description = "Second sequence description" };
        var original = await test.SaveAsync(new(1, "EU", Eu: new([first, second])));
        var tracking = new[] { Text("track-b"), Text("track-a"), Text("track-b") };
        var names = new[] { Text("Product B"), Text("Product A") };
        var inns = new[] { Text("active two"), Text("active one") };
        var defaults = await test.Service.SaveApplicationAsync(test.App.Id, 0, test.Snapshot.Id,
            new(1, "EU", Eu: new("Declared applicant", tracking, names, inns)));
        var inherited = await test.Service.InheritAsync(test.App.Id, "0001", await test.RevisionAsync(), defaults.Id);
        Assert.Equal(defaults.Id, inherited.InheritedDefaultsId); Assert.Equal(defaults.Digest, inherited.InheritedDefaultsDigest);
        Assert.Equal(new[] { first.Id, second.Id }, inherited.Content!.Eu!.Envelopes.Select(item => item.Id));
        for (var index = 0; index < 2; index++)
        {
            var expected = index == 0 ? first : second;
            var envelope = inherited.Content.Eu.Envelopes[index];
            Assert.Equal("Declared applicant", envelope.Applicant);
            Assert.Equal(tracking, envelope.ProcedureTrackingNumbers); Assert.Equal(names, envelope.InventedNames); Assert.Equal(inns, envelope.Inns);
            Assert.Equal(expected.Identifier, envelope.Identifier); Assert.Equal(expected.Country, envelope.Country); Assert.Equal(expected.Agency, envelope.Agency);
            Assert.Equal(expected.SubmissionNumber, envelope.SubmissionNumber); Assert.Equal(expected.SubmissionUnit, envelope.SubmissionUnit);
            Assert.Equal(expected.SubmissionMode, envelope.SubmissionMode); Assert.Equal(expected.RelatedSequences, envelope.RelatedSequences);
            Assert.Equal(expected.Description, envelope.Description);
        }
        Assert.Contains(inherited.Provenance, source => source.Origin == "ApplicationDefaults" && source.SourceRevisionId == defaults.Id && source.SourceDigest == defaults.Digest);
        Assert.All(inherited.Provenance.Where(source => source.Origin == "ApplicationDefaults"), source => Assert.Equal("Unconfirmed", source.Status));
        var exactInherited = JsonSerializer.Serialize(inherited);
        var changedDefaults = await test.Service.SaveApplicationAsync(test.App.Id, defaults.MetadataRevision, test.Snapshot.Id,
            new(1, "EU", Eu: new("Changed later applicant", [Text("different tracking")], [Text("Different name")], [])));
        Assert.NotEqual(defaults.Id, changedDefaults.Id);
        Assert.Equal(exactInherited, JsonSerializer.Serialize(await test.Service.GetSequenceAsync(test.App.Id, "0001")));
        Assert.Equal(exactInherited, JsonSerializer.Serialize(await test.Service.GetRevisionAsync(inherited.Id)));
        Assert.Equal(first.Applicant, (await test.Service.GetRevisionAsync(original.Id))!.Content!.Eu!.Envelopes[0].Applicant);
        Assert.Equal(inherited.WorkspaceRevision, await test.RevisionAsync()); Assert.False(inherited.IsReady);
        await test.EvidenceAsync("explicit-defaults", new { inherited.Id, inherited.Digest, sourceDefaults = defaults.Id,
            laterDefaults = changedDefaults.Id, envelopeOrder = inherited.Content.Eu.Envelopes.Select(item => item.Id) });
    }

    [Fact]
    public async Task TwoPlacementsOfOneDocumentKeepIndependentContextAndExplicitNodeCopiesDoNotDrift()
    {
        var test = await RegionalMetadataIntegrationRig.CreateAsync();
        Assert.Equal(test.First.DocumentId, test.Second.DocumentId); Assert.NotEqual(test.First.Id, test.Second.Id);
        var content = new RegionalMetadataContent(1, "EU", Eu: new([Envelope("de", "DE-BFARM", "initial", "0001")]));
        var original = await test.SaveAsync(content,
            [new(test.NodeId, "common", "en", "combined", null)],
            [new(test.First.Id, test.NodeId, "fr", "fr", "pl", null), new(test.Second.Id, test.NodeId, "common", "en", "combined", "previous-form-value")]);
        Assert.Equal(ExpectedValues1, original.DocumentContexts.Select(item => item.Country));
        var secondInherited = await test.Service.CopyNodeContextAsync(test.App.Id, "0001", await test.RevisionAsync(), original.Id, test.NodeId, [test.Second.Id]);
        Assert.Null(secondInherited.DocumentContexts.Single(item => item.PlacementId == test.Second.Id).FormType);
        var nextNodeSource = await test.SaveAsync(content, [new(test.NodeId, "de", "de", "spc", null)], secondInherited.DocumentContexts);
        var copied = await test.Service.CopyNodeContextAsync(test.App.Id, "0001", await test.RevisionAsync(), nextNodeSource.Id, test.NodeId, [test.First.Id]);
        Assert.Equal(new[] { test.First.Id, test.Second.Id }, copied.DocumentContexts.Select(item => item.PlacementId));
        var firstCopy = copied.DocumentContexts.Single(item => item.PlacementId == test.First.Id);
        var unaffected = copied.DocumentContexts.Single(item => item.PlacementId == test.Second.Id);
        Assert.Equal("de", firstCopy.Country); Assert.Equal("de", firstCopy.Language); Assert.Equal("spc", firstCopy.ProductInformationType);
        Assert.Equal("common", unaffected.Country); Assert.Equal("en", unaffected.Language); Assert.Equal("combined", unaffected.ProductInformationType);
        await test.EvidenceAsync("node-copy-provenance", new { copied.DocumentContexts, copied.Provenance });
        foreach (var field in new[] { "country", "language", "productInformationType", "formType" })
        {
            var firstEvidence = Assert.Single(copied.Provenance, item => item.FieldPath == "documentContexts[0]." + field);
            Assert.Equal("NodeDefaults", firstEvidence.Origin); Assert.Equal(nextNodeSource.Id, firstEvidence.SourceRevisionId); Assert.Equal(nextNodeSource.Digest, firstEvidence.SourceDigest);
            var secondEvidence = Assert.Single(copied.Provenance, item => item.FieldPath == "documentContexts[1]." + field);
            Assert.Equal("NodeDefaults", secondEvidence.Origin); Assert.Equal(original.Id, secondEvidence.SourceRevisionId); Assert.Equal(original.Digest, secondEvidence.SourceDigest);
        }
        var changed = await test.SaveAsync(content, [new(test.NodeId, "fr", "fr", "pl", null)], copied.DocumentContexts);
        Assert.Equal("de", changed.DocumentContexts.Single(item => item.PlacementId == test.First.Id).Country);
        Assert.Equal("fr", changed.NodeContexts.Single().Country);
        Assert.Equal("de", (await test.Service.GetRevisionAsync(copied.Id))!.DocumentContexts.Single(item => item.PlacementId == test.First.Id).Country);
        Assert.Equal("fr", (await test.Service.GetRevisionAsync(original.Id))!.DocumentContexts.Single(item => item.PlacementId == test.First.Id).Country);
        Assert.False(changed.IsReady);
        await test.EvidenceAsync("placement-context", new { document = test.First.DocumentId, firstPlacement = test.First.Id,
            secondPlacement = test.Second.Id, secondSource = original.Id, firstSource = nextNodeSource.Id, copied = copied.Id, later = changed.Id });
    }

    [Theory]
    [InlineData("foreign-application")]
    [InlineData("other-sequence")]
    [InlineData("mismatched-node")]
    [InlineData("missing-placement")]
    public async Task InvalidPlacementScopeCannotAppendARevisionOrAdvanceTheWorkspace(string problem)
    {
        var test = await RegionalMetadataIntegrationRig.CreateAsync();
        var original = await test.SaveAsync(new(1, "EU", Eu: new([Envelope("de", "DE-BFARM", "initial", "0001")])));
        var revision = await test.RevisionAsync();
        var placement = problem switch { "foreign-application" => test.Foreign.Id, "other-sequence" => test.Previous.Id,
            "missing-placement" => Guid.NewGuid(), _ => test.First.Id };
        var node = problem == "mismatched-node" ? test.OtherNodeId : test.NodeId;
        await Assert.ThrowsAsync<RegionalMetadataScopeException>(() => test.Service.SaveSequenceAsync(test.App.Id, "0001", revision,
            test.Snapshot.Id, original.Content!, documentContexts: [new(placement, node, "de", "de", "spc", null)]));
        Assert.Equal(revision, await test.RevisionAsync());
        Assert.Equal(original.Id, (await test.Service.GetSequenceAsync(test.App.Id, "0001"))!.Id);
        Assert.Equal(original.Digest, (await test.Service.GetRevisionAsync(original.Id))!.Digest);
    }

    [Fact]
    public async Task OrderedUsContactsAndTypedTelephonesRoundTripWithoutChangingLegacySharedFields()
    {
        var test = await RegionalMetadataIntegrationRig.CreateAsync(us: true);
        var legacy = SequencePublishingMetadata.Create("nda", "original-application", "application", "Legacy description", "Legacy Applicant", "fda-356h",
            "Legacy Contact", "regulatory", "5550000", "business", "legacy@example.test");
        var application = (await test.Applications.GetAsync(test.App.Id))!;
        application.Sequences.Single(sequence => sequence.SequenceNumber == "0001").RevisePublishingMetadata(legacy);
        await test.Applications.UpdateAsync(application);
        var contacts = new[]
        {
            Contact("Second contact", "technical", [("5550201", "business"), ("5550202", "fax")], ["second-b@example.test", "second-a@example.test"]),
            Contact("First contact", "regulatory", [("5550101", "business"), ("5550102", "mobile")], ["first@example.test", "alternate@example.test"])
        };
        var content = new RegionalMetadataContent(1, "US", Us: new("000001", "nda", "Structured Applicant", contacts,
            "original-application", "application", "fda-356h", "Structured description"));
        var saved = await test.SaveAsync(content);
        var read = (await test.Service.GetRevisionAsync(saved.Id))!;
        Assert.Equal(contacts.Select(contact => contact.Id), read.Content!.Us!.Contacts.Select(contact => contact.Id));
        Assert.Equal(ExpectedValues2, read.Content.Us.Contacts.Select(contact => contact.Name));
        Assert.Equal(ExpectedValues3, read.Content.Us.Contacts[0].Phones.Select(phone => phone.Type));
        Assert.Equal(ExpectedValues4, read.Content.Us.Contacts[0].Phones.Select(phone => phone.Number));
        Assert.Equal(ExpectedValues5, read.Content.Us.Contacts[0].Emails.Select(email => email.Value));
        contacts[0] = contacts[0] with { Name = "Caller mutated after save", Phones = [] };
        Assert.Equal("Second contact", (await test.Service.GetRevisionAsync(saved.Id))!.Content!.Us!.Contacts[0].Name);
        Assert.Equal(legacy, (await test.Applications.GetAsync(test.App.Id))!.Sequences.Single(sequence => sequence.SequenceNumber == "0001").PublishingMetadata);
        Assert.False(saved.IsReady);
    }

    [Fact]
    public async Task ReorderingEqualContactValuesKeepsProvenanceAttachedToStableContactIdentities()
    {
        var test = await RegionalMetadataIntegrationRig.CreateAsync(us: true);
        var inheritedContact = Contact("Same name", "regulatory", [("5550100", "business")], ["same@example.test"]);
        var enteredContact = Contact("Same name", "regulatory", [("5550100", "business")], ["same@example.test"]);
        Assert.NotEqual(inheritedContact.Id, enteredContact.Id);
        await test.SaveAsync(new(1, "US", Us: new("000001", "nda", "Applicant", [], "original-application", "application", null, "Sequence description")));
        var defaults = await test.Service.SaveApplicationAsync(test.App.Id, 0, test.Snapshot.Id,
            new(1, "US", Us: new("000001", "nda", "Applicant", [inheritedContact])));
        var inherited = await test.Service.InheritAsync(test.App.Id, "0001", await test.RevisionAsync(), defaults.Id);
        var withEntered = await test.SaveAsync(inherited.Content! with { Us = inherited.Content!.Us! with { Contacts = [inheritedContact, enteredContact] } });
        var reordered = await test.SaveAsync(withEntered.Content! with { Us = withEntered.Content!.Us! with { Contacts = [enteredContact, inheritedContact] } });
        Assert.Equal(new[] { enteredContact.Id, inheritedContact.Id }, reordered.Content!.Us!.Contacts.Select(contact => contact.Id));
        foreach (var field in new[] { "name", "type", "phones[0].number", "phones[0].type", "emails[0].value" })
        {
            var entered = Assert.Single(reordered.Provenance, item => item.FieldPath == "us.contacts[0]." + field);
            Assert.Equal("UserEntered", entered.Origin); Assert.Null(entered.SourceRevisionId); Assert.Null(entered.SourceDigest);
            var source = Assert.Single(reordered.Provenance, item => item.FieldPath == "us.contacts[1]." + field);
            Assert.Equal("ApplicationDefaults", source.Origin); Assert.Equal(defaults.Id, source.SourceRevisionId); Assert.Equal(defaults.Digest, source.SourceDigest);
        }
        Assert.Equal(new[] { inheritedContact.Id, enteredContact.Id }, (await test.Service.GetRevisionAsync(withEntered.Id))!.Content!.Us!.Contacts.Select(contact => contact.Id));
    }

    [Theory]
    [InlineData("initial", "0001")]
    [InlineData("reformat", "0001")]
    [InlineData("response", "0000")]
    public async Task RelatedSequencesFollowThePinnedInitialReformatAndLaterActivityRules(string unit, string related)
    {
        var test = await RegionalMetadataIntegrationRig.CreateAsync();
        var envelope = Envelope("de", "DE-BFARM", unit, related);
        var saved = await test.SaveAsync(new(1, "EU", Eu: new([envelope])));
        Assert.Equal(related, Assert.Single(saved.Content!.Eu!.Envelopes[0].RelatedSequences).SequenceNumber);
        Assert.DoesNotContain(saved.Diagnostics, issue => issue.FieldPath.Contains("relatedSequences", StringComparison.Ordinal));
        Assert.False(saved.IsReady);
    }

    [Fact]
    public async Task ExistingButInapplicableRelatedSequenceStaysVisibleWhileFutureAndMissingReferencesAreRejected()
    {
        var test = await RegionalMetadataIntegrationRig.CreateAsync();
        var saved = await test.SaveAsync(new(1, "EU", Eu: new([Envelope("de", "DE-BFARM", "response", "0001")])));
        Assert.Equal("0001", saved.Content!.Eu!.Envelopes[0].RelatedSequences[0].SequenceNumber);
        Assert.Contains(saved.Diagnostics, issue => issue.Code == "RelatedSequenceNotApplicable" &&
            issue.FieldPath == "eu.envelopes[0].relatedSequences[0].sequenceNumber");
        var revision = await test.RevisionAsync();
        foreach (var value in new[] { "0002", "9999" })
        {
            await Assert.ThrowsAsync<RegionalMetadataScopeException>(() => test.Service.SaveSequenceAsync(test.App.Id, "0001", revision,
                test.Snapshot.Id, new(1, "EU", Eu: new([Envelope("de", "DE-BFARM", "response", value)]))));
            Assert.Equal(revision, await test.RevisionAsync());
            Assert.Equal(saved.Id, (await test.Service.GetSequenceAsync(test.App.Id, "0001"))!.Id);
        }
    }

    [Fact]
    public async Task LegacyEuUpgradeRetainsOriginalSharedEvidenceAndReplaysWithoutInventingEnvelopeDefaults()
    {
        var test = await RegionalMetadataIntegrationRig.CreateAsync();
        var shared = SequencePublishingMetadata.Create("original application type", "original-application", "application",
            "Original shared description", "Original shared applicant", null, "Original contact", "regulatory",
            "5550100", "business", "legacy-eu@example.test");
        var application = (await test.Applications.GetAsync(test.App.Id))!;
        application.Sequences.Single(sequence => sequence.SequenceNumber == "0001").RevisePublishingMetadata(shared);
        await test.Applications.UpdateAsync(application);
        var expectedRevision = await test.RevisionAsync();
        var first = await test.Service.UpgradeLegacyAsync(test.App.Id, "0001", expectedRevision);
        Assert.Empty(first.Content!.Eu!.Envelopes);
        Assert.NotNull(first.LegacyJson); Assert.NotNull(first.LegacyDigest);
        using var raw = JsonDocument.Parse(first.LegacyJson);
        Assert.Equal(first.LegacyDigest, CanonicalJson.Digest(raw.RootElement));
        Assert.Contains("Original shared description", first.LegacyJson, StringComparison.Ordinal);
        Assert.Contains("Original shared applicant", first.LegacyJson, StringComparison.Ordinal);
        Assert.Contains("legacy-eu@example.test", first.LegacyJson, StringComparison.Ordinal);
        Assert.Contains(first.Diagnostics, issue => issue.Code == "LegacyDefaultsUnconfirmed");
        Assert.DoesNotContain(first.Provenance, item => item.Status == "Confirmed");
        Assert.Contains(first.Provenance, item => item.Origin == "LegacyUnknown");
        var after = await test.RevisionAsync(); Assert.Equal(expectedRevision + 1, after);
        var replay = await test.Service.UpgradeLegacyAsync(test.App.Id, "0001", expectedRevision);
        Assert.Equal(first.Id, replay.Id); Assert.Equal(first.Digest, replay.Digest); Assert.Equal(first.LegacyJson, replay.LegacyJson);
        Assert.Equal(after, await test.RevisionAsync());
        Assert.Equal(shared, (await test.Applications.GetAsync(test.App.Id))!.Sequences.Single(sequence => sequence.SequenceNumber == "0001").PublishingMetadata);

        // A later legacy-only update must not erase the independently persisted
        // regional head. This uses the existing shared-domain setter, not a new
        // controller or import path belonging to a later roadmap item.
        application = (await test.Applications.GetAsync(test.App.Id))!;
        application.Sequences.Single(sequence => sequence.SequenceNumber == "0001").RevisePublishingMetadata(shared with { SequenceDescription = "Later shared-only edit" });
        await test.Applications.UpdateAsync(application);
        Assert.Equal(first.Id, (await test.Service.GetSequenceAsync(test.App.Id, "0001"))!.Id);
        Assert.Equal(first.LegacyJson, (await test.Service.GetRevisionAsync(first.Id))!.LegacyJson);
        await test.EvidenceAsync("legacy-repeat", new { first.Id, first.Digest, first.LegacyDigest,
            initialRevision = expectedRevision, currentRevision = after, envelopeCount = first.Content.Eu.Envelopes.Count });
    }

    private static RegionalTextEntry Text(string value) => new(Guid.NewGuid(), value);
    private static EuEnvelope Envelope(string country, string agency, string unit, string related) => new(Guid.NewGuid(), Guid.NewGuid().ToString("D"),
        country, agency, "national", "maa", null, null, unit, "Original applicant", [Text("tracking-original")],
        [Text("Original product")], [], [new(Guid.NewGuid(), related)], "Original description");
    private static RegionalContact Contact(string name, string type, (string Number, string Type)[] phones, string[] emails) =>
        new(Guid.NewGuid(), name, type, phones.Select(phone => new RegionalTelephone(Guid.NewGuid(), phone.Number, phone.Type)).ToArray(),
            emails.Select(email => new RegionalEmail(Guid.NewGuid(), email)).ToArray());
}

internal sealed class RegionalMetadataIntegrationRig
{
    private static readonly JsonSerializerOptions CachedJsonOptions1 = new JsonSerializerOptions { WriteIndented = true };

    public required string Root { get; init; }
    public required SubmissionApplication App { get; init; }
    public required SubmissionApplication OtherApp { get; init; }
    public required InMemoryApplicationRepository Applications { get; init; }
    public required InMemoryDocumentPlacementRepository Placements { get; init; }
    public required InMemoryCtdNodeRepository Nodes { get; init; }
    public required InMemoryWorkspaceRevisionStore Revisions { get; init; }
    public required InMemoryRegionalMetadataStore Store { get; init; }
    public required RegionalMetadataService Service { get; init; }
    public required StandardsProfileSnapshot Snapshot { get; init; }
    public required Guid NodeId { get; init; }
    public required Guid OtherNodeId { get; init; }
    public required DocumentPlacement First { get; init; }
    public required DocumentPlacement Second { get; init; }
    public required DocumentPlacement Previous { get; init; }
    public required DocumentPlacement Foreign { get; init; }

    public static async Task<RegionalMetadataIntegrationRig> CreateAsync(bool us = false)
    {
        var root = Path.GetFullPath(".artifacts/p603-integration/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var template = us ? EctdTemplateRegistry.DefaultTemplateKey : EctdTemplateRegistry.EuTemplateKey;
        var app = new SubmissionApplication("000001", us ? "US" : "EU", "Synthetic Sponsor", root, template);
        app.CreateSequence("0000", "initial", "Earlier activity");
        app.CreateSequence("0001", "initial", "Metadata integration");
        app.CreateSequence("0002", "initial", "Existing future activity");
        var other = new SubmissionApplication("000002", us ? "US" : "EU", "Other Synthetic Sponsor", Path.Combine(root, "other"), template);
        other.CreateSequence("0001", "initial", "Foreign scope");
        var applications = new InMemoryApplicationRepository(); await applications.AddAsync(app); await applications.AddAsync(other);
        var revisions = new InMemoryWorkspaceRevisionStore(applications);
        var placements = new InMemoryDocumentPlacementRepository();
        var finalizations = new InMemoryFinalizationStore();
        var nodes = new InMemoryCtdNodeRepository(revisions, placements, finalizations: finalizations);
        var graph = new CtdNodeGraph(app.Id, IchSectionDefinitions.Current);
        var moduleOne = graph.Create("m1-administrative-information-and-prescribing-information", null, new Dictionary<string, string>());
        var moduleTwo = graph.Create("m2-common-technical-document-summaries", null, new Dictionary<string, string>());
        foreach (var sequence in new[] { "0000", "0001" })
            await nodes.SaveSequenceAsync(graph, sequence, graph.Nodes.Values.Select(node => new SequenceNode(graph, sequence,
                node.Id, node.IdentityAttributes, null, 0)).ToArray(), 0);
        var otherGraph = new CtdNodeGraph(other.Id, IchSectionDefinitions.Current);
        var foreignNode = otherGraph.Create("m1-administrative-information-and-prescribing-information", null, new Dictionary<string, string>());
        await nodes.SaveSequenceAsync(otherGraph, "0001", [new(otherGraph, "0001", foreignNode.Id, foreignNode.IdentityAttributes, null, 0)], 0);
        var documentBytes = Encoding.UTF8.GetBytes("Synthetic source whose bytes are shared by different placements.");
        var path = Path.Combine(root, "source.txt"); await File.WriteAllBytesAsync(path, documentBytes);
        var document = new SubmissionDocument("source.txt", "text/plain", documentBytes.Length,
            Convert.ToHexString(SHA256.HashData(documentBytes)).ToLowerInvariant(), Convert.ToHexString(MD5.HashData(documentBytes)).ToLowerInvariant(), path);
        var documents = new InMemoryDocumentRepository(); await documents.AddAsync(document);
        async Task<DocumentPlacement> Mount(SubmissionApplication owner, string sequence, string leaf, CtdNodeGraph ownerGraph, Guid nodeId)
        {
            var placement = new DocumentPlacement(document.Id, owner.Id, sequence, "m1", DocumentPlacementOperation.New, leaf, leaf);
            placement.BindToNode(new(ownerGraph, sequence, nodeId, ownerGraph.Get(nodeId).IdentityAttributes, null, 0), 0);
            await placements.AddAsync(placement); return placement;
        }
        var first = await Mount(app, "0001", "first", graph, moduleOne.Id);
        var second = await Mount(app, "0001", "second", graph, moduleOne.Id);
        var previous = await Mount(app, "0000", "previous", graph, moduleOne.Id);
        var foreign = await Mount(other, "0001", "foreign", otherGraph, foreignNode.Id);
        var snapshotService = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new CompositeStandardsProfileProvider([new FdaEctd322StandardsProfileProvider(), new EuEctd322StandardsProfileProvider()]));
        var snapshot = await snapshotService.CaptureAsync(template);
        var transactions = new InMemoryPersistenceTransaction(); var store = new InMemoryRegionalMetadataStore();
        var service = new RegionalMetadataService(store, applications, nodes, placements, snapshotService,
            new WorkspaceMutationCoordinator(revisions, transactions, finalizedGuard: new FinalizedWorkspaceGuard(finalizations)), revisions, transactions);
        return new() { Root = root, App = app, OtherApp = other, Applications = applications, Placements = placements, Nodes = nodes,
            Revisions = revisions, Store = store, Service = service, Snapshot = snapshot, NodeId = moduleOne.Id,
            OtherNodeId = moduleTwo.Id, First = first, Second = second, Previous = previous, Foreign = foreign };
    }
    public async Task<long> RevisionAsync(string sequence = "0001") => (await Revisions.GetRevisionAsync(App.Id, sequence))!.Value;
    public async Task<RegionalMetadataRevision> SaveAsync(RegionalMetadataContent content, IReadOnlyList<RegionalNodeContext>? nodes = null,
        IReadOnlyList<RegionalDocumentContext>? documents = null) => await Service.SaveSequenceAsync(App.Id, "0001", await RevisionAsync(), Snapshot.Id, content, nodes, documents);
    public Task EvidenceAsync(string name, object evidence) => File.WriteAllTextAsync(Path.Combine(Root, name + ".json"),
        JsonSerializer.Serialize(evidence, CachedJsonOptions1));
}
