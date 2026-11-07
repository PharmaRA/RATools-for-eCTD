using System.Text.Json;
using RATools.Api.Contracts;
using RATools.Application.Applications;
using RATools.Application.Applications.Requests;
using RATools.Application.RegionalMetadata;
using RATools.Application.Standards;
using RATools.Application.Workspaces;
using RATools.Application.Finalization;
using RATools.Application.PackageValidation;
using RATools.Domain.Applications;
using RATools.Domain.RegionalMetadata;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.RegionalMetadata;

public sealed class RegionalMetadataEditingTests
{
    private static readonly JsonSerializerOptions CachedJsonOptions1 = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions CachedJsonOptions2 = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private static readonly JsonSerializerOptions CachedJsonOptions3 = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private static readonly string[] ExpectedValues4 = new[] { "initial", "reformat" };
    private static readonly string[] ExpectedValues5 = new[] { "m1.3.1" };

    [Theory]
    [InlineData("{\"expectedRevision\":0,\"expectedRevision\":1}")]
    [InlineData("{\"expectedRevision\":0,\"provenance\":[]}")]
    [InlineData("{\"expectedRevision\":0,\"regional\":{\"standardsSnapshotId\":\"x\",\"content\":{\"schemaVersion\":1,\"regionKey\":\"EU\",\"eu\":{\"envelopes\":[],\"envelopes\":[]}}}}")]
    [InlineData("{\"expectedRevision\":0,\"regional\":{\"standardsSnapshotId\":\"x\",\"content\":{\"schemaVersion\":1,\"regionKey\":\"EU\",\"eu\":{\"envelopes\":[],\"verified\":true}}}}")]
    [InlineData("{\"ExpectedRevision\":0}")]
    public void PublishingRequestRejectsAmbiguousOrClientSuppliedEvidence(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UpdateSequencePublishingMetadataRequestBody>(json,
            CachedJsonOptions1));
    }

    [Fact]
    public async Task MixedSaveCommitsOneWorkspaceRevisionAndSharedOnlyUpdatePreservesRegionalHead()
    {
        var test = await Rig.CreateAsync();
        var updated = await test.Shared.UpdateAsync(test.App.Id, "0001", Request(0) with { Regional = new(test.SnapshotId, Content()) });
        Assert.Equal(1, updated!.WorkspaceRevision); Assert.Equal("Saved applicant", updated.ApplicantName);
        Assert.NotNull(updated.Regional); Assert.Equal(1, updated.Regional.MetadataRevision);
        var regionalId = updated.Regional.Id;
        var shared = await test.Shared.UpdateAsync(test.App.Id, "0001", Request(1) with { ApplicantName = "Shared only" });
        Assert.Equal(2, shared!.WorkspaceRevision);
        Assert.Equal(regionalId, (await test.Shared.GetAsync(test.App.Id, "0001"))!.Regional!.Id);
        Assert.Equal(1, (await test.Service.GetSequenceAsync(test.App.Id, "0001"))!.WorkspaceRevision);
    }

    [Fact]
    public async Task MixedAppendFailureRollsBackSharedRegionalAndWorkspaceTogether()
    {
        var test = await Rig.CreateAsync(failAppend: true);
        await Assert.ThrowsAsync<IOException>(() => test.Shared.UpdateAsync(test.App.Id, "0001", Request(0) with { Regional = new(test.SnapshotId, Content()) }));
        Assert.Null((await test.Applications.GetAsync(test.App.Id))!.Sequences.Single().PublishingMetadata);
        Assert.Null(await test.Service.GetSequenceAsync(test.App.Id, "0001"));
        Assert.Equal(0, await test.Revisions.GetRevisionAsync(test.App.Id, "0001"));
    }

    [Fact]
    public async Task UnsavedPreflightRetainsInvalidValuesAndDoesNotCreateHeadsOrChangeRevision()
    {
        var test = await Rig.CreateAsync();
        var content = Content() with { Eu = new([Envelope() with { Country = "invalid-country", Applicant = null }]) };
        var result = await test.Service.PreflightAsync(test.App.Id, "0001", new(test.SnapshotId, content));
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "ControlledValueNotSupported" && diagnostic.FieldPath == "eu.envelopes[0].country" && diagnostic.Source!.Sha256.Length == 64);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RegionalQualificationPending");
        Assert.Contains(result.Summary, field => field.FieldPath == "content.eu.envelopes[0].country" && field.Value == "\"invalid-country\"");
        var defaults = await test.Service.PreflightDefaultsAsync(test.App.Id, test.SnapshotId, new(1, "EU", Eu: new(null, [], [], [])));
        Assert.Contains(defaults.Diagnostics, diagnostic => diagnostic.FieldPath == "eu.applicant");
        Assert.Null(await test.Service.GetApplicationAsync(test.App.Id)); Assert.Null(await test.Service.GetSequenceAsync(test.App.Id, "0001"));
        Assert.Equal(0, await test.Revisions.GetRevisionAsync(test.App.Id, "0001"));
        Assert.Equal("invalid-country", content.Eu!.Envelopes[0].Country);
    }

    [Fact]
    public async Task ExactDefaultsPreviewMatchesMutationAndHasConcreteFieldDiff()
    {
        var test = await Rig.CreateAsync();
        var defaults = await test.Service.SaveApplicationAsync(test.App.Id, 0, test.SnapshotId, new(1, "EU", Eu: new("Inherited applicant", [], [], [])));
        var before = await test.Service.SaveSequenceAsync(test.App.Id, "0001", 0, test.SnapshotId, Content());
        var preview = await test.Service.PreviewInheritanceAsync(test.App.Id, "0001", 1, defaults.Id);
        Assert.Equal(defaults.Id, preview.SourceRevisionId); Assert.Equal(defaults.Digest, preview.SourceDigest);
        Assert.Contains(preview.FieldDiffs, field => field.FieldPath == "content.eu.envelopes[0].applicant" && field.Before == "\"Original\"" && field.After == "\"Inherited applicant\"");
        Assert.Equal(before.Id, (await test.Service.GetSequenceAsync(test.App.Id, "0001"))!.Id); Assert.Equal(1, await test.Revisions.GetRevisionAsync(test.App.Id, "0001"));
        var after = await test.Service.InheritAsync(test.App.Id, "0001", 1, defaults.Id);
        Assert.Equal(JsonSerializer.Serialize(preview.ProposedContent), JsonSerializer.Serialize(after.Content));
        Assert.Equal(2, await test.Revisions.GetRevisionAsync(test.App.Id, "0001"));
        await Assert.ThrowsAsync<RegionalMetadataScopeException>(() => test.Service.GetScopedRevisionAsync(test.App.Id, "0001", defaults.Id));
    }

    [Fact]
    public async Task SchemaUsesCapturedEnvelopeAndContextVocabulariesAndKeepsFreeTextExplicit()
    {
        var test = await Rig.CreateAsync(); var schema = await test.Service.GetSchemaAsync(test.App.Id, test.SnapshotId);
        Assert.Equal(test.SnapshotId, schema.StandardsSnapshotId);
        var envelope = schema.SequenceFields.Single(field => field.Key == "eu").Fields.Single(field => field.Key == "envelopes");
        var country = envelope.Fields.Single(field => field.Key == "country");
        Assert.Contains("fr", country.Options); Assert.DoesNotContain("common", country.Options);
        Assert.Equal("xml-eu-envelope", country.Source!.ResourceKey); Assert.Equal(64, country.Source.Sha256.Length);
        var contextCountry = schema.NodeContextFields.Single(field => field.Key == "country");
        Assert.Contains("common", contextCountry.Options); Assert.Equal("xml-eu-dtd", contextCountry.Source!.ResourceKey);
        Assert.Equal("id", envelope.IdentityField); Assert.Equal("RegionalQualificationPending", schema.QualificationStatus);
        var us = await Rig.CreateAsync(us: true); var usSchema = await us.Service.GetSchemaAsync(us.App.Id, us.SnapshotId);
        var applicationType = usSchema.SequenceFields.Single(field => field.Key == "us").Fields.Single(field => field.Key == "applicationType");
        Assert.Equal("text", applicationType.Kind); Assert.Empty(applicationType.Options); Assert.NotNull(applicationType.BusinessEvidence);
        Assert.Equal("application-number/@application-type", applicationType.Source!.Locator);
    }

    [Fact]
    public async Task RelatedSequenceConditionIsGuidanceSoLaterActivitiesRemainEditable()
    {
        var test = await Rig.CreateAsync(); var schema = await test.Service.GetSchemaAsync(test.App.Id, test.SnapshotId);
        var related = schema.SequenceFields.Single(field => field.Key == "eu").Fields.Single(field => field.Key == "envelopes")
            .Fields.Single(field => field.Key == "relatedSequences");
        Assert.True(related.Required);
        var condition = Assert.Single(related.Conditions!);
        var json = JsonSerializer.SerializeToElement(condition, CachedJsonOptions2);
        Assert.True(json.TryGetProperty("effect", out var effect), "The schema must distinguish guidance from visibility.");
        Assert.Equal("guidance", effect.GetString());
        Assert.Equal("eu.envelopes[].submissionUnit", condition.FieldPath);
        Assert.Equal(ExpectedValues4, condition.Values);
    }

    [Theory]
    [InlineData("country", "required")]
    [InlineData("language", "required")]
    [InlineData("productInformationType", "required,visible")]
    public async Task ContextConditionsSeparateOptionalFieldsFromPiVisibility(string key, string effects)
    {
        var test = await Rig.CreateAsync(); var schema = await test.Service.GetSchemaAsync(test.App.Id, test.SnapshotId);
        foreach (var context in new[] { schema.NodeContextFields, schema.DocumentContextFields })
        {
            var field = context.Single(item => item.Key == key);
            Assert.False(field.Required);
            var actual = field.Conditions!.Select(condition =>
            {
                Assert.Equal("ctdSection", condition.FieldPath); Assert.Equal(ExpectedValues5, condition.Values);
                var json = JsonSerializer.SerializeToElement(condition, CachedJsonOptions3);
                Assert.True(json.TryGetProperty("effect", out var effect), "Context applicability must specify its effect.");
                return effect.GetString();
            }).Order(StringComparer.Ordinal);
            Assert.Equal(effects.Split(','), actual);
        }
    }

    [Theory]
    [InlineData("defaults")]
    [InlineData("defaults-preflight")]
    [InlineData("mixed")]
    [InlineData("preflight")]
    public async Task XmlIllegalTextProducesArgumentErrorAndLeavesAllHeadsAndRevisionUntouched(string operation)
    {
        var test = await Rig.CreateAsync();
        var content = Content() with { Eu = new([Envelope() with { Applicant = "Invalid\0applicant" }]) };
        var defaults = new RegionalApplicationDefaultsContent(1, "EU", Eu: new("Invalid\0applicant", [], [], []));
        Func<Task> action = operation switch
        {
            "defaults" => async () => { await test.Service.SaveApplicationAsync(test.App.Id, 0, test.SnapshotId, defaults); },
            "defaults-preflight" => async () => { await test.Service.PreflightDefaultsAsync(test.App.Id, test.SnapshotId, defaults); },
            "mixed" => async () => { await test.Shared.UpdateAsync(test.App.Id, "0001", Request(0) with { Regional = new(test.SnapshotId, content) }); },
            _ => async () => { await test.Service.PreflightAsync(test.App.Id, "0001", new(test.SnapshotId, content)); }
        };
        var error = await Assert.ThrowsAsync<ArgumentException>(action); Assert.IsType<System.Xml.XmlException>(error.InnerException);
        Assert.Null(await test.Service.GetApplicationAsync(test.App.Id)); Assert.Null(await test.Service.GetSequenceAsync(test.App.Id, "0001"));
        Assert.Equal(0, await test.Revisions.GetRevisionAsync(test.App.Id, "0001"));
        Assert.Null((await test.Applications.GetAsync(test.App.Id))!.Sequences.Single().PublishingMetadata);
    }

    [Fact]
    public void XmlIllegalTextCannotEnterImmutableRevisionEvidence()
    {
        var invalid = Content() with { Eu = new([Envelope() with { Applicant = "Invalid\0applicant" }]) };
        var error = Assert.Throws<ArgumentException>(() => RegionalMetadataRevision.Create(Guid.NewGuid(), Guid.NewGuid(), "0001", 1, 1,
            "sha256:" + new string('a', 64), invalid, null, [], [], null, null, [], [], null, null, DateTime.UtcNow));
        Assert.IsType<System.Xml.XmlException>(error.InnerException);
    }

    [Fact]
    public async Task MixedResponseKeepsItsCommittedValuesWhenAnotherWriterAdvancesBeforeHeadRead()
    {
        var test = await Rig.CreateAsync(); var laterWriterRan = false;
        test.Store.OnCommittedRead = async () =>
        {
            laterWriterRan = true;
            await test.Shared.UpdateAsync(test.App.Id, "0001", Request(1) with
            {
                ApplicantName = "Later writer", Regional = new(test.SnapshotId, Content() with { Eu = new([Envelope() with { Applicant = "Later regional" }]) })
            });
        };
        var first = (await test.Shared.UpdateAsync(test.App.Id, "0001", Request(0) with { Regional = new(test.SnapshotId, Content()) }))!;
        // If the implementation returns the committed result directly, run the later
        // writer at the next reader instead. Both paths compare after writer B committed.
        _ = await test.Service.GetSequenceAsync(test.App.Id, "0001");
        Assert.True(laterWriterRan); Assert.Equal(2, await test.Revisions.GetRevisionAsync(test.App.Id, "0001"));
        Assert.Equal(1, first.WorkspaceRevision); Assert.Equal("Saved applicant", first.ApplicantName);
        Assert.Equal(1, first.Regional!.MetadataRevision); Assert.Equal(1, first.Regional.WorkspaceRevision);
        Assert.Equal("Original", first.Regional.Content!.Eu!.Envelopes[0].Applicant);
        Assert.Equal("Later regional", (await test.Service.GetSequenceAsync(test.App.Id, "0001"))!.Content!.Eu!.Envelopes[0].Applicant);
    }

    [Fact]
    public async Task MixedMutationsRequireCurrentRevisionAndRefuseFinalizedWorkspace()
    {
        var test = await Rig.CreateAsync();
        var update = Request(0) with { Regional = new(test.SnapshotId, Content()) };
        await Assert.ThrowsAsync<WorkspaceRevisionRequiredException>(() => test.Shared.UpdateAsync(test.App.Id, "0001", update with { ExpectedRevision = null }));
        await Assert.ThrowsAsync<RATools.Application.Abstractions.Persistence.WorkspaceRevisionConflictException>(() => test.Shared.UpdateAsync(test.App.Id, "0001", update with { ExpectedRevision = 1 }));
        await test.Shared.UpdateAsync(test.App.Id, "0001", update);
        var saved = (await test.Service.GetSequenceAsync(test.App.Id, "0001"))!;
        await test.Finalizations.RegisterAsync(FinalizedSequence.Create(test.App.Id, "0001", 1, new string('a', 64), PackageValidationCatalog.EuProfile, "3.2.2", "3.1.1",
            new string('b', 64), Guid.NewGuid(), new string('c', 64), new string('d', 64), PackageValidationMode.Development, false, [], FinalizationService.ToolVersion, "p604-freeze", DateTime.UtcNow));
        await Assert.ThrowsAsync<WorkspaceFinalizedException>(() => test.Shared.UpdateAsync(test.App.Id, "0001", update with { ExpectedRevision = 1 }));
        Assert.Equal(1, await test.Revisions.GetRevisionAsync(test.App.Id, "0001")); Assert.Equal(saved.Id, (await test.Service.GetSequenceAsync(test.App.Id, "0001"))!.Id);
    }

    [Fact]
    public async Task NodeCopyPreviewMatchesExactSourceMutationAndRejectsForeignTargetsWithoutWriting()
    {
        var test = await RegionalMetadataIntegrationRig.CreateAsync();
        var saved = await test.SaveAsync(Content(), [new(test.NodeId, "fr", null, null, null)], [new(test.First.Id, test.NodeId, "de", null, null, null)]);
        var revision = await test.RevisionAsync();
        var preview = await test.Service.PreviewNodeContextCopyAsync(test.App.Id, "0001", revision, saved.Id, test.NodeId, [test.First.Id]);
        Assert.Equal("fr", preview.DocumentContexts.Single().Country);
        Assert.Contains(preview.FieldDiffs, field => field.FieldPath == "documentContexts[0].country" && field.Before == "\"de\"" && field.After == "\"fr\"");
        Assert.Equal(saved.Id, (await test.Service.GetSequenceAsync(test.App.Id, "0001"))!.Id); Assert.Equal(revision, await test.RevisionAsync());
        await Assert.ThrowsAsync<RegionalMetadataScopeException>(() => test.Service.PreviewNodeContextCopyAsync(test.App.Id, "0001", revision, saved.Id, test.NodeId, [test.Foreign.Id]));
        await Assert.ThrowsAsync<RegionalMetadataScopeException>(() => test.Service.PreflightAsync(test.App.Id, "0001", new(test.Snapshot.Id, Content(), DocumentContexts: [new(test.Previous.Id, test.NodeId, "fr", null, null, null)])));
        Assert.Equal(revision, await test.RevisionAsync());
        var copy = await test.Service.CopyNodeContextAsync(test.App.Id, "0001", revision, saved.Id, test.NodeId, [test.First.Id]);
        Assert.Equal(JsonSerializer.Serialize(preview.DocumentContexts), JsonSerializer.Serialize(copy.DocumentContexts));
        Assert.Equal(saved.Id, Assert.Single(copy.Provenance, field => field.FieldPath == "documentContexts[0].country").SourceRevisionId);
    }

    [Fact]
    public async Task ConditionalProgramAndContextChangesKeepSavedValuesForCorrection()
    {
        var test = await RegionalMetadataIntegrationRig.CreateAsync();
        var saved = await test.SaveAsync(Content(), [new(test.NodeId, "fr", "en", "invalid-pi", "old-form")]);
        var updated = saved.Content! with { Eu = new([saved.Content!.Eu!.Envelopes[0] with { Procedure = "centralised", SubmissionUnit = "response" }]) };
        var next = await test.Service.SaveSequenceAsync(test.App.Id, "0001", await test.RevisionAsync(), test.Snapshot.Id, updated);
        Assert.Equal("invalid-pi", next.NodeContexts.Single().ProductInformationType); Assert.Equal("old-form", next.NodeContexts.Single().FormType);
        Assert.Equal(saved.Content.Eu.Envelopes[0].Agency, next.Content!.Eu!.Envelopes[0].Agency);
        Assert.Contains(next.Diagnostics, issue => issue.Code == "ProductInformationContextNotApplicable" && issue.FieldPath == "nodeContexts[0].productInformationType");
        Assert.Contains(next.Diagnostics, issue => issue.Code == "RelatedSequenceNotApplicable");
    }

    [Fact]
    public async Task DefaultsWithoutAHeadReturnsExplicitNotFound()
    {
        var test = await Rig.CreateAsync(); var controller = new RegionalMetadataController(test.Service);
        Assert.IsType<NotFoundObjectResult>(await controller.Defaults(test.App.Id, CancellationToken.None));
    }

    [Fact]
    public async Task NullCopyTargetsReturnBadRequestWithoutWriting()
    {
        var test = await RegionalMetadataIntegrationRig.CreateAsync();
        var saved = await test.SaveAsync(Content(), [new(test.NodeId, "fr", null, null, null)]);
        var copy = await new RegionalMetadataController(test.Service).Copy(test.App.Id, "0001", new() { ExpectedRevision = await test.RevisionAsync(), SourceRevisionId = saved.Id, NodeInstanceId = test.NodeId, PlacementIds = null! }, CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(copy);
    }

    [Fact]
    public async Task LegacyHttpUpgradeStillRequiresExpectedRevisionWhenReplayExists()
    {
        var test = await Rig.CreateAsync(); await test.Service.UpgradeLegacyAsync(test.App.Id, "0001", 0);
        await Assert.ThrowsAsync<WorkspaceRevisionRequiredException>(() => new RegionalMetadataController(test.Service).Upgrade(test.App.Id, "0001", new(), CancellationToken.None));
    }

    private static UpdateSequencePublishingMetadataRequest Request(long expected) => new(null, "initial", null, "Saved description", "Saved applicant", null, null, null, null, null, null, expected);
    private static RegionalMetadataContent Content() => new(1, "EU", Eu: new([Envelope()]));
    private static EuEnvelope Envelope() => new(Guid.NewGuid(), "identifier", "fr", "FR-AFSSAPS", "national", "maa", null, null, "initial", "Original",
        [new(Guid.NewGuid(), "track")], [new(Guid.NewGuid(), "Product")], [], [new(Guid.NewGuid(), "0001")], "Description");
    internal sealed record Rig(SubmissionApplication App, InMemoryApplicationRepository Applications, InMemoryWorkspaceRevisionStore Revisions,
        RegionalMetadataService Service, SequencePublishingMetadataService Shared, string SnapshotId, InMemoryFinalizationStore Finalizations, InterleavingStore Store)
    {
        public static async Task<Rig> CreateAsync(bool failAppend = false, bool us = false)
        {
            var app = new SubmissionApplication("p604-" + Guid.NewGuid().ToString("N"), us ? "US" : "EU", "Original sponsor",
                Path.GetFullPath(".artifacts/p604-service/" + Guid.NewGuid().ToString("N")), us ? "us-fda-ectd-3.2.2" : "eu-ectd-3.2.2");
            app.CreateSequence("0001", "initial", "Original description");
            var applications = new InMemoryApplicationRepository(); await applications.AddAsync(app);
            var revisions = new InMemoryWorkspaceRevisionStore(applications); var transaction = new InMemoryPersistenceTransaction();
            var finalizations = new InMemoryFinalizationStore();
            var mutations = new WorkspaceMutationCoordinator(revisions, transaction, finalizedGuard: new FinalizedWorkspaceGuard(finalizations)); var placements = new InMemoryDocumentPlacementRepository();
            var profiles = new CompositeStandardsProfileProvider([new FdaEctd322StandardsProfileProvider(), new EuEctd322StandardsProfileProvider()]);
            var snapshots = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(), profiles);
            var snapshot = await snapshots.CaptureAsync(app.EctdTemplateKey); var store = new InterleavingStore(new InMemoryRegionalMetadataStore());
            var service = new RegionalMetadataService(failAppend ? new FailingStore(store) : store, applications, new InMemoryCtdNodeRepository(revisions, placements), placements, snapshots, mutations, revisions, transaction);
            return new(app, applications, revisions, service, new SequencePublishingMetadataService(applications, profiles, mutations, service), snapshot.Id, finalizations, store);
        }
    }
    internal sealed class InterleavingStore(IRegionalMetadataStore inner) : IRegionalMetadataStore
    {
        public Func<Task>? OnCommittedRead { get; set; }
        public async Task<RegionalMetadataRevision?> GetHeadAsync(Guid applicationId, string? sequenceNumber, CancellationToken cancellationToken = default)
        {
            var head = await inner.GetHeadAsync(applicationId, sequenceNumber, cancellationToken);
            if (head is not null && sequenceNumber is not null && OnCommittedRead is { } callback)
            {
                OnCommittedRead = null; await callback();
                return await inner.GetHeadAsync(applicationId, sequenceNumber, cancellationToken);
            }
            return head;
        }
        public Task<RegionalMetadataRevision?> GetRevisionAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetRevisionAsync(id, cancellationToken);
        public Task<RegionalMetadataRevision?> FindLegacyAsync(Guid applicationId, string sequenceNumber, string legacyDigest, CancellationToken cancellationToken = default) => inner.FindLegacyAsync(applicationId, sequenceNumber, legacyDigest, cancellationToken);
        public Task<RegionalMetadataRevision> AppendAsync(RegionalMetadataRevision revision, Guid? expectedHeadId, CancellationToken cancellationToken = default) => inner.AppendAsync(revision, expectedHeadId, cancellationToken);
    }
    private sealed class FailingStore(IRegionalMetadataStore inner) : IRegionalMetadataStore
    {
        public Task<RegionalMetadataRevision?> GetHeadAsync(Guid applicationId, string? sequenceNumber, CancellationToken cancellationToken = default) => inner.GetHeadAsync(applicationId, sequenceNumber, cancellationToken);
        public Task<RegionalMetadataRevision?> GetRevisionAsync(Guid id, CancellationToken cancellationToken = default) => inner.GetRevisionAsync(id, cancellationToken);
        public Task<RegionalMetadataRevision?> FindLegacyAsync(Guid applicationId, string sequenceNumber, string legacyDigest, CancellationToken cancellationToken = default) => inner.FindLegacyAsync(applicationId, sequenceNumber, legacyDigest, cancellationToken);
        public async Task<RegionalMetadataRevision> AppendAsync(RegionalMetadataRevision revision, Guid? expectedHeadId, CancellationToken cancellationToken = default)
        { await inner.AppendAsync(revision, expectedHeadId, cancellationToken); throw new IOException("Injected mixed save append failure."); }
    }
}
