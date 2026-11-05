using RATools.Application.RegionalMetadata;
using RATools.Application.Standards;
using RATools.Application.Workspaces;
using RATools.Domain.Applications;
using RATools.Domain.RegionalMetadata;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Finalization;
using RATools.Application.Ctd;
using RATools.Domain.Ctd;
using RATools.Domain.Documents;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Application.PackageValidation;

namespace RATools.Tests.RegionalMetadata;

public sealed class RegionalMetadataServiceTests
{
    [Fact]
    public async Task FinalizedDraftRefusesRegionalSaveInheritanceAndLegacyUpgrade()
    {
        var finalizations = new InMemoryFinalizationStore(); var test = await Rig.CreateAsync(finalizations: new FinalizedWorkspaceGuard(finalizations));
        var defaults = await test.Service.SaveApplicationAsync(test.App.Id, 0, test.SnapshotId, new(1, "EU", Eu: new("Applicant", [], [], [])));
        await finalizations.RegisterAsync(FinalizedSequence.Create(test.App.Id, "0001", 0, new string('a', 64), PackageValidationCatalog.EuProfile, "3.2.2", "3.1.1",
            new string('b', 64), Guid.NewGuid(), new string('c', 64), new string('d', 64), PackageValidationMode.Development, false, [], FinalizationService.ToolVersion, "regional-freeze", DateTime.UtcNow));
        await Assert.ThrowsAsync<WorkspaceFinalizedException>(() => test.Service.SaveSequenceAsync(test.App.Id, "0001", 0, test.SnapshotId, new(1, "EU", Eu: new([]))));
        await Assert.ThrowsAsync<WorkspaceFinalizedException>(() => test.Service.InheritAsync(test.App.Id, "0001", 0, defaults.Id));
        await Assert.ThrowsAsync<WorkspaceFinalizedException>(() => test.Service.UpgradeLegacyAsync(test.App.Id, "0001", 0));
        Assert.Null(await test.Service.GetSequenceAsync(test.App.Id, "0001")); Assert.Equal(0, await test.Revisions.GetRevisionAsync(test.App.Id, "0001"));
    }
    [Fact]
    public async Task ReorderingEqualValuesDoesNotMoveInheritedProvenanceToAnotherStableEntry()
    {
        var test = await Rig.CreateAsync(); var inherited = new RegionalTextEntry(Guid.NewGuid(), "Same value");
        var defaults = await test.Service.SaveApplicationAsync(test.App.Id, 0, test.SnapshotId,
            new(1, "EU", Eu: new("Inherited Applicant", [new(Guid.NewGuid(), "track")], [inherited], [])));
        var envelope = RegionalMetadataModelTests.Envelope();
        var first = await test.Service.SaveSequenceAsync(test.App.Id, "0001", 0, test.SnapshotId, new(1, "EU", Eu: new([envelope])));
        var copy = await test.Service.InheritAsync(test.App.Id, "0001", first.WorkspaceRevision, defaults.Id);
        var user = new RegionalTextEntry(Guid.NewGuid(), "Same value");
        var edited = copy.Content! with { Eu = new([copy.Content.Eu!.Envelopes[0] with { InventedNames = [inherited, user] }]) };
        var second = await test.Service.SaveSequenceAsync(test.App.Id, "0001", copy.WorkspaceRevision, test.SnapshotId, edited);
        var reordered = edited with { Eu = new([edited.Eu!.Envelopes[0] with { InventedNames = [user, inherited] }]) };
        var final = await test.Service.SaveSequenceAsync(test.App.Id, "0001", second.WorkspaceRevision, test.SnapshotId, reordered);
        var firstOrigin = Assert.Single(final.Provenance, evidence => evidence.FieldPath == "eu.envelopes[0].inventedNames[0].value");
        var secondOrigin = Assert.Single(final.Provenance, evidence => evidence.FieldPath == "eu.envelopes[0].inventedNames[1].value");
        Assert.Equal("UserEntered", firstOrigin.Origin); Assert.Null(firstOrigin.SourceRevisionId);
        Assert.Equal("ApplicationDefaults", secondOrigin.Origin); Assert.Equal(defaults.Id, secondOrigin.SourceRevisionId); Assert.Equal(defaults.Digest, secondOrigin.SourceDigest);
    }

    [Fact]
    public async Task CopyingOneNodeDefaultPreservesPlacementOrderAndTheOtherPlacementsSource()
    {
        var test = await Rig.CreateAsync(); var graph = new CtdNodeGraph(test.App.Id, IchSectionDefinitions.Current);
        var quality = graph.Create("m3-quality", null, new Dictionary<string, string>()); var body = graph.Create("m3-2-body-of-data", quality.Id, new Dictionary<string, string>());
        var substance = graph.Create("m3-2-s-drug-substance", body.Id, new Dictionary<string, string> { ["substance"] = "Synthetic", ["manufacturer"] = "One" });
        var control = graph.Create("m3-2-s-4-control-of-drug-substance", substance.Id, new Dictionary<string, string>());
        var leaf = graph.Create("m3-2-s-4-1-specification", control.Id, new Dictionary<string, string>());
        var sequenceNodes = graph.Nodes.Values.Select(node => new SequenceNode(graph, "0001", node.Id, node.IdentityAttributes)).ToArray();
        await test.Nodes.SaveSequenceAsync(graph, "0001", sequenceNodes, 0);
        var document = Guid.NewGuid(); var a = new DocumentPlacement(document, test.App.Id, "0001", "m3.2.s.4.1", DocumentPlacementOperation.New, "A");
        var b = new DocumentPlacement(document, test.App.Id, "0001", "m3.2.s.4.1", DocumentPlacementOperation.New, "B");
        a.BindToNode(sequenceNodes.Single(node => node.NodeInstanceId == leaf.Id)); b.BindToNode(sequenceNodes.Single(node => node.NodeInstanceId == leaf.Id));
        await test.Placements.AddAsync(a); await test.Placements.AddAsync(b);
        var first = await test.Service.SaveSequenceAsync(test.App.Id, "0001", 1, test.SnapshotId, new(1, "EU", Eu: new([RegionalMetadataModelTests.Envelope()])),
            [new(leaf.Id, "fr", null, null, null)], [new(a.Id, leaf.Id, "de", null, null, null), new(b.Id, leaf.Id, "de", null, null, null)]);
        var copyA = await test.Service.CopyNodeContextAsync(test.App.Id, "0001", first.WorkspaceRevision, first.Id, leaf.Id, [a.Id]);
        Assert.Equal(new[] { a.Id, b.Id }, copyA.DocumentContexts.Select(item => item.PlacementId));
        var next = await test.Service.SaveSequenceAsync(test.App.Id, "0001", copyA.WorkspaceRevision, test.SnapshotId, copyA.Content!, [new(leaf.Id, "de", null, null, null)]);
        Assert.Equal(first.Id, Assert.Single(next.Provenance, evidence => evidence.FieldPath == "documentContexts[0].formType").SourceRevisionId);
        var copyB = await test.Service.CopyNodeContextAsync(test.App.Id, "0001", next.WorkspaceRevision, next.Id, leaf.Id, [b.Id]);
        var final = await test.Service.CopyNodeContextAsync(test.App.Id, "0001", copyB.WorkspaceRevision, first.Id, leaf.Id, [a.Id]);
        Assert.Equal(new[] { a.Id, b.Id }, final.DocumentContexts.Select(item => item.PlacementId));
        Assert.Equal(first.Id, Assert.Single(final.Provenance, evidence => evidence.FieldPath == "documentContexts[0].country").SourceRevisionId);
        Assert.Equal(next.Id, Assert.Single(final.Provenance, evidence => evidence.FieldPath == "documentContexts[1].country").SourceRevisionId);
        Assert.Equal("fr", final.DocumentContexts[0].Country); Assert.Equal("de", final.DocumentContexts[1].Country);
    }

    [Fact]
    public async Task FailureAfterAppendLeavesNoHeadOrWorkspaceRevisionChange()
    {
        var test = await Rig.CreateAsync(failAppend: true);
        await Assert.ThrowsAsync<IOException>(() => test.Service.SaveSequenceAsync(test.App.Id, "0001", 0, test.SnapshotId,
            new(1, "EU", Eu: new([RegionalMetadataModelTests.Envelope()]))));
        Assert.Null(await test.Service.GetSequenceAsync(test.App.Id, "0001")); Assert.Equal(0, await test.Revisions.GetRevisionAsync(test.App.Id, "0001"));
    }
    [Fact]
    public async Task DraftSaveRecordsDiagnosticsAndOneWorkspaceRevisionThenRejectsStaleSave()
    {
        var test = await Rig.CreateAsync();
        var metadata = new RegionalMetadataContent(1, "EU", Eu: new([RegionalMetadataModelTests.Envelope() with { Country = "common", Applicant = null }]));
        var saved = await test.Service.SaveSequenceAsync(test.App.Id, "0001", 0, test.SnapshotId, metadata);
        Assert.Equal(1, saved.WorkspaceRevision); Assert.Equal(1, saved.MetadataRevision);
        Assert.Contains(saved.Diagnostics, issue => issue.FieldPath == "eu.envelopes[0].country");
        Assert.Contains(saved.Diagnostics, issue => issue.FieldPath == "eu.envelopes[0].applicant");
        Assert.Equal(saved.Id, (await test.Service.GetSequenceAsync(test.App.Id, "0001"))!.Id);
        await Assert.ThrowsAsync<RATools.Application.Abstractions.Persistence.WorkspaceRevisionConflictException>(() => test.Service.SaveSequenceAsync(test.App.Id, "0001", 0, test.SnapshotId, metadata));
    }

    [Fact]
    public async Task LegacyEuUpgradeIsIdempotentAndDoesNotInventDynamicWriterDefaults()
    {
        var test = await Rig.CreateAsync();
        var first = await test.Service.UpgradeLegacyAsync(test.App.Id, "0001", 0);
        var again = await test.Service.UpgradeLegacyAsync(test.App.Id, "0001", 0);
        Assert.Equal(first.Id, again.Id); Assert.Equal(1, await test.Revisions.GetRevisionAsync(test.App.Id, "0001"));
        Assert.Empty(first.Content!.Eu!.Envelopes); Assert.NotNull(first.LegacyJson);
        Assert.DoesNotContain("centralised", first.LegacyJson, StringComparison.Ordinal);
        Assert.Contains(first.Diagnostics, issue => issue.Code == "LegacyDefaultsUnconfirmed");
        Assert.All(first.Provenance, evidence => Assert.Equal("LegacyUnknown", evidence.Origin));
    }

    [Fact]
    public async Task ARegionalPayloadCannotBeBoundToTheOtherTemplatesSnapshot()
    {
        var test = await Rig.CreateAsync();
        await Assert.ThrowsAsync<RegionalMetadataScopeException>(() => test.Service.SaveSequenceAsync(test.App.Id, "0001", 0, test.SnapshotId,
            new(1, "US", Us: new(null, null, null, [], null, null, null, null))));
        Assert.Null(await test.Service.GetSequenceAsync(test.App.Id, "0001"));
    }

    private sealed record Rig(SubmissionApplication App, InMemoryWorkspaceRevisionStore Revisions, RegionalMetadataService Service, string SnapshotId,
        InMemoryCtdNodeRepository Nodes, InMemoryDocumentPlacementRepository Placements)
    {
        public static async Task<Rig> CreateAsync(bool failAppend = false, FinalizedWorkspaceGuard? finalizations = null)
        {
            var app = new SubmissionApplication("regional-" + Guid.NewGuid().ToString("N"), "EU", "Synthetic", Path.GetFullPath(".artifacts/p603-regional-tests/" + Guid.NewGuid().ToString("N")), "eu-ectd-3.2.2");
            app.CreateSequence("0001", "initial", "Regional metadata"); var applications = new InMemoryApplicationRepository(); await applications.AddAsync(app);
            var revisions = new InMemoryWorkspaceRevisionStore(applications); var placements = new InMemoryDocumentPlacementRepository(); var nodes = new InMemoryCtdNodeRepository(revisions, placements);
            var transaction = new InMemoryPersistenceTransaction(); var snapshots = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), new EuEctd322StandardsProfileProvider());
            var snapshot = await snapshots.CaptureAsync(app.EctdTemplateKey);
            var store = new InMemoryRegionalMetadataStore();
            var service = new RegionalMetadataService(failAppend ? new FailingStore(store) : store, applications, nodes, placements, snapshots,
                new WorkspaceMutationCoordinator(revisions, transaction, finalizedGuard: finalizations), revisions, transaction);
            return new(app, revisions, service, snapshot.Id, nodes, placements);
        }
    }
    private sealed class FailingStore(IRegionalMetadataStore inner) : IRegionalMetadataStore
    {
        public Task<RegionalMetadataRevision?> GetHeadAsync(Guid applicationId, string? sequenceNumber, CancellationToken cancellationToken = default) => inner.GetHeadAsync(applicationId, sequenceNumber, cancellationToken);
        public Task<RegionalMetadataRevision?> GetRevisionAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetRevisionAsync(id, cancellationToken);
        public Task<RegionalMetadataRevision?> FindLegacyAsync(Guid applicationId, string sequenceNumber, string legacyDigest, CancellationToken cancellationToken = default) => inner.FindLegacyAsync(applicationId, sequenceNumber, legacyDigest, cancellationToken);
        public async Task<RegionalMetadataRevision> AppendAsync(RegionalMetadataRevision revision, Guid? expectedHeadId, CancellationToken cancellationToken = default)
        { await inner.AppendAsync(revision, expectedHeadId, cancellationToken); throw new IOException("Injected failure after staging metadata append."); }
    }
}
