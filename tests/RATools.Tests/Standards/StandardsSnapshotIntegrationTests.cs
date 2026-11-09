using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Security;
using RATools.Application.Abstractions.Persistence;
using RATools.Application.Applications.EctdTemplates;
using RATools.Application.Ctd;
using RATools.Application.Documents;
using RATools.Application.Finalization;
using RATools.Application.PackageValidation;
using RATools.Application.Publishing.Ich;
using RATools.Application.Publishing.PackageModel;
using RATools.Application.Publishing.Regions;
using RATools.Application.Publishing.UsRegional;
using RATools.Application.Standards;
using RATools.Domain.Applications;
using RATools.Domain.Common;
using RATools.Domain.Ctd;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Domain.Standards;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.PackageValidation;

namespace RATools.Tests.Standards;

public sealed class StandardsSnapshotIntegrationTests
{
    // Recorded by the pre-change d9dbd4e stage7 assemblies, before the snapshot
    // fields existed. See .artifacts/p602-legacy-baseline/evidence.json. These
    // literal expectations deliberately do not use today's hash implementation
    // to generate both sides of the compatibility assertion.
    private const string LegacyBinding = """
        {"inputManifestDigest":"c96c6d5be8d08a12e7b5cdc1b207fa6b2430974c86803d8891675e76fd992c20","historyBaselineDigest":"cb91f5f8592e7cf177e8331fd7da393fdcbe3d85658f733a34850b99676f1f30","profileSnapshotId":"us-fda-3.2.2-m1-3.3-development-v1","rulesDigest":"8c12facb2fdc7485e8f0a3385e30fd39e0289fc0ca324ee7e0974e897b0482bb","engineVersion":"snapshot-legacy-engine-v1","validationMode":"Development","resourceLimitsDigest":"99289b6b11f15897b459c0cb50cee7f2b010f1214593e7601fc686a89b8329aa"}
        """;
    private const string LegacyManifest = """
        {"content":{"applicationId":"20000000-0000-0000-0000-000000000001","assets":[],"files":[{"kind":"Payload","length":8,"md5":"4dd72b9887b48c6fcd22be192abd528d","relativePath":"payload/0001/index.xml","sha256":"387d4263efccd8a5c4876d8fcfe5530c4c4bce225bf9968a686a4ace703c27e8"},{"kind":"ValidationReport","length":2,"md5":"99914b932bd37a50b983c5e7c90ae93b","relativePath":"validation-report.json","sha256":"44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a"}],"historyBaseline":{"applicationId":"20000000-0000-0000-0000-000000000001","digest":"cb91f5f8592e7cf177e8331fd7da393fdcbe3d85658f733a34850b99676f1f30","entries":[],"targetSequence":"0001"},"leaves":[],"lifecycle":[],"nodes":[],"sequenceNumber":"0001","validatedInputManifestDigest":"c96c6d5be8d08a12e7b5cdc1b207fa6b2430974c86803d8891675e76fd992c20","validation":{"comparisonInputDigest":null,"engineVersion":"snapshot-legacy-engine-v1","ichEctdVersion":"3.2.2","regionalModule1Version":"3.3","reportSha256":"44136fa355b3678a1146ad16f7e8649e94fb4fc21fe77e8310c060f61caaff8a","rulesDigest":"8c12facb2fdc7485e8f0a3385e30fd39e0289fc0ca324ee7e0974e897b0482bb","standardsProfileSnapshotId":"us-fda-3.2.2-m1-3.3-development-v1","validationMode":"Development"}},"createdByToolVersion":"ratools-finalization-v1","digest":"7e018b67500470f054b334ba325ae731be1dc3b1d6457975d42e11ab9e176a79","schemaVersion":1,"sourceWorkspaceRevision":7}
        """;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter() } };

    [Fact]
    public void LegacyBindingsRequestsAndReleaseManifestsKeepTheirPreSnapshotDigests()
    {
        var binding = JsonSerializer.Deserialize<ValidationBinding>(LegacyBinding, Json)!;
        Assert.Equal("76266656cef2ab2202afa2ad8b4824c4913b51ccb0354b3e9f804a93d6269486", Sha(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(binding, Json))));
        var application = Guid.Parse("20000000-0000-0000-0000-000000000001");
        var run = new PackageValidationRun(application, "0001", binding, "{\"schemaVersion\":1,\"legacyFixture\":true}",
            "legacy-minimal-request", new(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc));
        Assert.Equal("757c9da669503b6815d73201f167c8a83ff7359442d0abe0fa881bae9fa645a0", run.RequestDigest);
        run.VerifyRequestIntegrity();
        var storedRequest = new StoredPackageValidationRequest(1, Guid.Parse("20000000-0000-0000-0000-000000000002"), null, [],
            JsonSerializer.SerializeToElement(new { fixture = "legacy-catalog" }), JsonSerializer.SerializeToElement(new { fixture = "legacy-baseline" }), new());
        Assert.Equal("0c818a36dbc29196c33f30612b7e4cc9bb4e3cd7a32e3a8a1221ebb8b651e90a", Sha(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(storedRequest, Json))));
        const string digest = "7e018b67500470f054b334ba325ae731be1dc3b1d6457975d42e11ab9e176a79";
        var manifest = ReleaseManifest.FromJson(Encoding.UTF8.GetBytes(LegacyManifest), application, "0001", digest);
        Assert.Equal(digest, manifest.Digest);
        Assert.Equal("2870825a68fdc88fa85049cb73a410a245595a7a87e7eeffde174bb188ed01f7", Sha(manifest.ToUtf8Json()));
        Assert.Equal(LegacyManifest, Encoding.UTF8.GetString(manifest.ToUtf8Json()));
    }

    [Fact]
    public async Task ActualCapturedResourcesRemainReadableAfterProviderAndAssetFilesChange()
    {
        var fixture = await SnapshotFixture.CreateAsync();
        var before = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        Assert.StartsWith("sha256:", before.Id);
        var original = await fixture.Service.OpenResourceAsync(before.Id, "provider-ich-ectd-3-2-dtd");
        var ruleBytes = await fixture.Service.OpenResourceAsync(before.Id, "rules");
        using (var rules = new MemoryStream(ruleBytes.Bytes)) Assert.Equal(PackageValidationCatalog.Current.Digest, PackageValidationCatalog.Load(rules).Digest);
        Assert.Equal(await File.ReadAllBytesAsync(fixture.IchPath), original.Bytes);
        Assert.Equal(before.Id, (await fixture.Service.CaptureAsync(SnapshotFixture.Template)).Id);
        Assert.Equal(before.AcquiredUtc, (await fixture.Service.CaptureAsync(SnapshotFixture.Template)).AcquiredUtc);
        var originalBytes = original.Bytes.ToArray();

        await fixture.ChangeAssetAsync();
        var bytesOnly = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        Assert.NotEqual(before.Id, bytesOnly.Id);
        Assert.Equal(before.Descriptor.GuideVersion, bytesOnly.Descriptor.GuideVersion);
        fixture.Provider.GuideVersion = "synthetic-next-guide";
        var after = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        Assert.NotEqual(bytesOnly.Id, after.Id);
        Assert.NotEqual(before.Id, after.Id);
        Assert.NotEqual(before.Descriptor.GuideVersion, after.Descriptor.GuideVersion);
        Assert.NotEqual(original.Sha256, (await fixture.Service.OpenResourceAsync(after.Id, original.Key)).Sha256);
        fixture.Provider.RefuseReads = true;
        var restoredService = new StandardsSnapshotService(fixture.Store, fixture.Provider, new() { AssetRootPath = fixture.Root });
        var restored = await restoredService.GetAsync(before.Id);
        Assert.Equal(JsonSerializer.Serialize(before.Descriptor), JsonSerializer.Serialize(restored.Descriptor));
        Assert.Equal(originalBytes, (await restoredService.OpenResourceAsync(before.Id, original.Key)).Bytes);
        Assert.Equal(ruleBytes.Bytes, (await restoredService.OpenResourceAsync(before.Id, "rules")).Bytes);
        original.Bytes[0] ^= 0xff;
        Assert.Equal(originalBytes, (await restoredService.OpenResourceAsync(before.Id, original.Key)).Bytes);
        Assert.Contains(restored.Resources, resource => resource.Key == "xml-ich-dtd");
        Assert.Contains(restored.Resources, resource => resource.Key == "xml-manifest");
        Assert.Contains(restored.Resources, resource => resource.Key == "node-schema");
        Assert.Contains(restored.Resources, resource => resource.Key == "node-paths");
        await fixture.EvidenceAsync("upgrade", new { before.Id, changedAssetId = bytesOnly.Id, afterId = after.Id, oldAsset = original.Sha256,
            oldRuleDigest = Sha(ruleBytes.Bytes), oldSnapshotReadableWithoutProvider = true });
    }

    [Fact]
    public async Task CapturedResourcesRejectBytesOrMetadataThatNoLongerMatchTheirIdentity()
    {
        var fixture = await SnapshotFixture.CreateAsync();
        var snapshot = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        var resource = snapshot.Resources.Single(item => item.Key == "provider-ich-ectd-3-2-dtd");
        var changed = resource.ReadBytes(); changed[0] ^= 0xff;
        Assert.Throws<InvalidDataException>(() => new StandardsSnapshotResource(resource.Key, resource.Category,
            resource.RelativePath, resource.MediaType, resource.Length, resource.Sha256, Convert.ToBase64String(changed)));
        Assert.Throws<InvalidDataException>(() => new StandardsProfileSnapshot(snapshot.Id, snapshot.SchemaVersion,
            snapshot.Descriptor with { GuideVersion = "changed-under-the-same-identity" }, snapshot.Resources, snapshot.AcquiredUtc));
        var body = JsonSerializer.Serialize(snapshot);
        var changedBody = body.Replace(JsonSerializer.Serialize(resource.ContentBase64), JsonSerializer.Serialize(Convert.ToBase64String(changed)), StringComparison.Ordinal);
        Assert.NotEqual(body, changedBody);
        Assert.Throws<InvalidDataException>(() => JsonSerializer.Deserialize<StandardsProfileSnapshot>(changedBody));
        Assert.Equal(snapshot.Id, (await fixture.Service.GetAsync(snapshot.Id)).Id);
    }

    [Fact]
    public async Task BundledCapturedScopeNeverBecomesFormalMerelyBecauseResourcesWereFrozen()
    {
        var fixture = await SnapshotFixture.CreateAsync();
        var snapshot = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        var context = new StandardsBusinessContext("nda", "national", "US", "en", "m3", "new");
        var reasons = snapshot.Evaluate(context, new(2026, 11, 5), SnapshotFixture.Template);
        Assert.Contains(reasons, reason => reason.StartsWith("ProfileNotActive:", StringComparison.Ordinal));
        Assert.Contains(reasons, reason => reason.StartsWith("BusinessScopeNotQualified:", StringComparison.Ordinal));
        Assert.Contains(reasons, reason => reason.StartsWith("EvidenceIncomplete:", StringComparison.Ordinal));
        Assert.Equal("Verified", Assert.Single(snapshot.Descriptor.Evidence, evidence => evidence.Category == "Sources").Status);
        Assert.DoesNotContain(reasons, reason => reason.StartsWith("SourceVerificationIncomplete:", StringComparison.Ordinal));
        Assert.Contains("EvidenceIncomplete: Rules", reasons);
        Assert.Contains("EvidenceIncomplete: ExternalComparison", reasons);
        Assert.All(snapshot.Descriptor.Scopes, scope => { Assert.True(scope.CreateDraft); Assert.True(scope.TrialPublish); Assert.False(scope.Finalize); });
        Assert.Contains(snapshot.Evaluate(context, snapshot.Lifecycle.EffectiveFrom.AddDays(-1), SnapshotFixture.Template),
            reason => reason.StartsWith("ProfileDateNotApplicable:", StringComparison.Ordinal));
        Assert.Contains(snapshot.Evaluate(context, new(2026, 11, 5), "eu-ema-ectd-3.2.2"),
            reason => reason.StartsWith("TemplateMismatch:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ImplicitSelectionLostResponseReplayKeepsOriginalSnapshotAfterProviderUpgrade()
    {
        var fixture = await SnapshotFixture.CreateAsync();
        var validation = await ValidationFixture.CreateAsync(fixture);
        var request = new CreatePackageValidationRequest(validation.Input.Id, PackageValidationCatalog.UsProfile,
            PackageValidationMode.Development, "implicit-current");
        var first = await validation.Service.EnqueueAsync(request);
        Assert.NotNull(first.Run.Binding.StandardsSnapshotId);
        var original = first.Run.Binding.StandardsSnapshotId;
        await fixture.UpgradeAsync();
        var changed = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        Assert.NotEqual(original, changed.Id);
        fixture.Provider.RefuseReads = true;
        var replay = await validation.Service.EnqueueAsync(request);
        Assert.False(replay.Created); Assert.Equal(first.Run.Id, replay.Run.Id);
        Assert.Equal(original, replay.Run.Binding.StandardsSnapshotId);
        Assert.Equal(first.Run.RequestDigest, replay.Run.RequestDigest);
        await Assert.ThrowsAsync<PackageValidationIdempotencyConflictException>(() => validation.Service.EnqueueAsync(request with { StandardsSnapshotId = changed.Id }));
    }

    [Fact]
    public async Task ExplicitRunAndReleaseUseArchivedSnapshotAfterProviderAndSourceAssetsChange()
    {
        var fixture = await SnapshotFixture.CreateAsync();
        var validation = await ValidationFixture.CreateAsync(fixture);
        var snapshot = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        var originalAsset = await fixture.Service.OpenResourceAsync(snapshot.Id, "provider-ich-ectd-3-2-dtd");
        var queued = await validation.Service.EnqueueAsync(new(validation.Input.Id, PackageValidationCatalog.UsProfile,
            PackageValidationMode.Development, "explicit-old-snapshot", StandardsSnapshotId: snapshot.Id,
            StandardsBusinessContext: ValidationFixture.Context, SelectionDate: ValidationFixture.Today));
        Assert.Equal(snapshot.Id, queued.Run.Binding.StandardsSnapshotId);
        await fixture.UpgradeAsync();
        var newer = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        Assert.NotEqual(snapshot.Id, newer.Id);
        fixture.Provider.RefuseReads = true;
        var (run, report) = await validation.CompleteAsync(queued.Run);
        Assert.Equal(snapshot.Id, report.Binding.StandardsSnapshotId);
        Assert.Equal(ValidationFixture.Today, report.Binding.SelectionDate);
        Assert.Equal(CheckStatus.Pass, Assert.Single(report.Coverage, rule => rule.RuleId == "FDA47-2035").CheckStatus);
        Assert.Equal(CheckStatus.Pass, Assert.Single(report.Coverage, rule => rule.RuleId == "FDA47-2044").CheckStatus);
        Assert.Equal(CheckStatus.Pass, Assert.Single(report.Coverage, rule => rule.RuleId == "FDA47-2036").CheckStatus);
        Assert.Equal(ValidationFixture.Context, report.Binding.StandardsBusinessContext);
        Assert.Equal(queued.Run.RequestDigest, run.RequestDigest);
        Assert.False(report.IsReadyForFinalization);
        Assert.DoesNotContain(report.Coverage.SelectMany(rule => rule.Findings), finding => finding.CheckStatus == CheckStatus.Fail);
        var archived = (await validation.Store.GetReportJsonAsync(run.Id))!;
        Assert.Contains(snapshot.Id, archived, StringComparison.Ordinal);
        Assert.DoesNotContain(newer.Id, archived, StringComparison.Ordinal);

        var captures = new RecordingReleaseStore(fixture.Root);
        var planner = new EctdReleaseCapturePlanner(new FixedPackageBuilder(validation.Package), new IchIndexXmlWriter(),
            Registry(), fixture.Provider, snapshots: fixture.Service);
        var service = new ReleaseCaptureService(validation.Store, planner, captures, snapshots: fixture.Service);
        var capture = await service.CaptureAsync(new(validation.App.Id, "0001", 0, run.Id, PackageValidationCatalog.UsProfile,
            "3.2.2", "3.3", run.Binding.HistoryBaselineDigest, "captured-with-old-snapshot"));
        Assert.Equal(snapshot.Id, captures.Manifest!.Validation.StandardsSnapshotId);
        Assert.Equal(snapshot.Id, report.Binding.StandardsSnapshotId);
        Assert.Equal(originalAsset.Bytes, captures.Bytes["payload/0001/util/dtd/ich-ectd-3-2.dtd"]);
        Assert.Equal(run.ReportSha256, captures.Manifest.Validation.ReportSha256);
        Assert.Equal(archived, Encoding.UTF8.GetString(captures.Bytes["validation-report.json"]));
        Assert.Equal(captures.Manifest.Digest, capture.ManifestDigest);
        Assert.Equal(PackageValidationMode.Development, capture.ValidationMode);
        Assert.False(capture.ReportReady);
        await capture.PublishAsync();
        await fixture.EvidenceAsync("validation-release", new { snapshotId = snapshot.Id, currentId = newer.Id, runId = run.Id,
            run.RequestDigest, run.ReportSha256, capture.ManifestDigest, resourceSha256 = originalAsset.Sha256,
            mode = "Development", releaseStorageProvider = "retained-test-adapter" });
    }

    [Fact]
    public async Task FormalMissingOrUnqualifiedSnapshotIsRejectedWhileDevelopmentRetainsTrialScope()
    {
        var fixture = await SnapshotFixture.CreateAsync();
        var validation = await ValidationFixture.CreateAsync(fixture);
        var snapshot = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        var withoutSnapshots = new PackageValidationService(validation.Store, validation.Reader, validation.Paths,
            new(new PdfPigPackageInspector()), timeProvider: new FixedClock(), applications: validation.Applications);
        var formal = new CreatePackageValidationRequest(validation.Input.Id, PackageValidationCatalog.UsProfile,
            PackageValidationMode.Formal, "formal-missing", StandardsBusinessContext: ValidationFixture.Context,
            SelectionDate: ValidationFixture.Today);
        await Assert.ThrowsAsync<StandardsSelectionException>(() => withoutSnapshots.EnqueueAsync(formal));
        var rejected = await Assert.ThrowsAsync<StandardsSelectionException>(() => validation.Service.EnqueueAsync(
            formal with { IdempotencyKey = "formal-inactive", StandardsSnapshotId = snapshot.Id }));
        Assert.Contains(rejected.Reasons, reason => reason.StartsWith("ProfileNotActive:", StringComparison.Ordinal));
        var future = StandardsProfileSnapshot.Create(snapshot.Descriptor with
        { Lifecycle = snapshot.Lifecycle with { EffectiveFrom = ValidationFixture.Today.AddDays(1) } }, snapshot.Resources, snapshot.AcquiredUtc);
        await fixture.Store.AddAsync(future);
        var futureRejected = await Assert.ThrowsAsync<StandardsSelectionException>(() => validation.Service.EnqueueAsync(
            formal with { IdempotencyKey = "formal-future", StandardsSnapshotId = future.Id }));
        Assert.Contains(futureRejected.Reasons, reason => reason.StartsWith("ProfileDateNotApplicable:", StringComparison.Ordinal));
        var retired = StandardsProfileSnapshot.Create(snapshot.Descriptor with
        { Lifecycle = snapshot.Lifecycle with { RetiredFrom = ValidationFixture.Today, Status = StandardsSnapshotStatus.Retired } }, snapshot.Resources, snapshot.AcquiredUtc);
        await fixture.Store.AddAsync(retired);
        var retiredRejected = await Assert.ThrowsAsync<StandardsSelectionException>(() => validation.Service.EnqueueAsync(
            formal with { IdempotencyKey = "formal-retired", StandardsSnapshotId = retired.Id }));
        Assert.Contains(retiredRejected.Reasons, reason => reason.StartsWith("ProfileDateNotApplicable:", StringComparison.Ordinal));
        Assert.Contains(rejected.Reasons, reason => reason.StartsWith("BusinessScopeNotQualified:", StringComparison.Ordinal));
        var trial = await validation.Service.EnqueueAsync(formal with { Mode = PackageValidationMode.Development,
            IdempotencyKey = "trial-inactive", StandardsSnapshotId = snapshot.Id });
        var (_, report) = await validation.CompleteAsync(trial.Run);
        Assert.False(report.IsReadyForFinalization);
        Assert.Contains(report.BlockingReasons, reason => reason.StartsWith("DevelopmentMode:", StringComparison.Ordinal));
        Assert.Equal(PackageValidationCatalog.Current.ForProfile(PackageValidationCatalog.UsProfile).Select(rule => rule.InternalRuleId),
            report.Coverage.Select(rule => rule.RuleId));
    }

    [Fact]
    public async Task HistoricalSelectionKeepsItsOwnAssetsWhenTheSameAliasHasNewTargetBytes()
    {
        var fixture = await SnapshotFixture.CreateAsync();
        var historical = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        await fixture.UpgradeAsync();
        var current = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        var resources = current.Resources.ToDictionary(resource => resource.Key, StringComparer.Ordinal);
        var currentDtd = resources["provider-ich-ectd-3-2-dtd"].ReadBytes();
        var providerProfile = StandardsSnapshotService.Profile(current);
        Assert.Equal(Sha(currentDtd), providerProfile.Assets.Single(asset => asset.Key == "ich-ectd-3-2-dtd").Sha256);
        void Replace(string key, byte[] bytes)
        {
            var resource = resources[key];
            resources[key] = StandardsSnapshotResource.Create(key, resource.Category, resource.RelativePath, resource.MediaType, bytes);
        }
        // This synthetic second acquired snapshot changes real DTD bytes but
        // preserves the alias. Every embedded resource binding is updated so the
        // test exercises an internally consistent version, not damaged evidence.
        Replace("xml-ich-dtd", currentDtd);
        var xmlManifest = JsonNode.Parse(resources["xml-manifest"].ReadBytes())!;
        xmlManifest["assets"]!.AsArray().Single(asset => asset!["id"]!.GetValue<string>() == "ich-dtd")!["sha256"] = Sha(currentDtd);
        var manifestBytes = Encoding.UTF8.GetBytes(xmlManifest.ToJsonString()); Replace("xml-manifest", manifestBytes);
        var rules = JsonNode.Parse(resources["rules"].ReadBytes())!;
        rules["sources"]!.AsArray().Single(source => source!["id"]!.GetValue<string>() == "ratools-package-xml-assets-v1")!["sha256"] = Sha(manifestBytes);
        Replace("rules", Encoding.UTF8.GetBytes(rules.ToJsonString()));
        var target = StandardsProfileSnapshot.Create(current.Descriptor, resources.Values, current.AcquiredUtc);
        await fixture.Store.AddAsync(target);
        Assert.Equal(historical.RuleProfileAlias, target.RuleProfileAlias); Assert.NotEqual(historical.Id, target.Id);
        var validation = await ValidationFixture.CreateAsync(fixture);
        var history = await validation.RegisterHistoryAsync(historical);
        var queued = await validation.Service.EnqueueAsync(new(validation.Input.Id, PackageValidationCatalog.UsProfile,
            PackageValidationMode.Development, "independent-history-snapshot", History: [new(history.Id, PackageValidationCatalog.UsProfile, historical.Id)],
            StandardsSnapshotId: target.Id));
        fixture.Provider.RefuseReads = true;
        var (_, report) = await validation.CompleteAsync(queued.Run);
        Assert.Equal(target.Id, report.Binding.StandardsSnapshotId);
        Assert.NotEqual(PackageValidationCatalog.Current.Digest, report.Binding.RulesDigest);
        Assert.Contains(historical.Id, queued.Run.RequestJson, StringComparison.Ordinal);
        Assert.Contains(target.Id, queued.Run.RequestJson, StringComparison.Ordinal);
        Assert.DoesNotContain(report.Coverage.SelectMany(rule => rule.Findings), finding => finding.CheckStatus == CheckStatus.Fail);
        Assert.False(report.IsReadyForFinalization);
        await fixture.EvidenceAsync("independent-history", new { historySnapshot = historical.Id, targetSnapshot = target.Id,
            oldDtd = historical.Resources.Single(resource => resource.Key == "xml-ich-dtd").Sha256,
            newDtd = resources["xml-ich-dtd"].Sha256, report.Binding.RulesDigest });
    }

    [Fact]
    public async Task LegacyHistoryRemainsUnverifiedInDevelopmentAndFormalAdmissionIsRejected()
    {
        var fixture = await SnapshotFixture.CreateAsync();
        var snapshot = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        var validation = await ValidationFixture.CreateAsync(fixture);
        var historical = await validation.RegisterHistoryAsync(snapshot);
        var request = new CreatePackageValidationRequest(validation.Input.Id, PackageValidationCatalog.UsProfile,
            PackageValidationMode.Development, "legacy-history", History: [new(historical.Id, PackageValidationCatalog.UsProfile)], StandardsSnapshotId: snapshot.Id);
        var queued = await validation.Service.EnqueueAsync(request);
        var (_, report) = await validation.CompleteAsync(queued.Run);
        Assert.Equal(CheckStatus.NotEvaluated, Assert.Single(report.Coverage, rule => rule.RuleId == "HISTORY-BASELINE").CheckStatus);
        Assert.Contains("Unverified", queued.Run.RequestJson, StringComparison.Ordinal);
        Assert.False(report.IsReadyForFinalization);
        await Assert.ThrowsAsync<StandardsSelectionException>(() => validation.Service.EnqueueAsync(request with
        { Mode = PackageValidationMode.Formal, IdempotencyKey = "formal-legacy-history", StandardsBusinessContext = ValidationFixture.Context, SelectionDate = ValidationFixture.Today }));
    }

    [Fact]
    public async Task CaptureRefusesAnEuTemplateBoundToTheUsRuleAlias()
    {
        var service = new StandardsSnapshotService(new InMemoryStandardsSnapshotStore(),
            new CompositeStandardsProfileProvider([new FdaEctd322StandardsProfileProvider(), new EuEctd322StandardsProfileProvider()]));
        var eu = await service.CaptureAsync(EctdTemplateRegistry.EuTemplateKey);
        Assert.Equal(PackageValidationCatalog.EuProfile, eu.RuleProfileAlias);
        var error = await Record.ExceptionAsync(() => service.CaptureAsync(EctdTemplateRegistry.EuTemplateKey, PackageValidationCatalog.UsProfile));
        Assert.NotNull(error);
        Assert.True(error is StandardsSelectionException or ArgumentException, error.ToString());
    }

    [Fact]
    public async Task LegacyQueuedVerifiedHistoryRetainsItsArchivedBaselineAfterSnapshotInjection()
    {
        var fixture = await SnapshotFixture.CreateAsync();
        var validation = await ValidationFixture.CreateAsync(fixture);
        var historyResources = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        var historical = await validation.RegisterHistoryAsync(historyResources);
        var finalizations = new InMemoryFinalizationStore();
        var resolver = new FinalizedHistoryResolver(finalizations);
        var legacy = new PackageValidationService(validation.Store, validation.Reader, validation.Paths,
            new(new PdfPigPackageInspector()), timeProvider: new FixedClock(), finalizedHistory: resolver);
        var historyQueued = await legacy.EnqueueAsync(new(historical.Id, PackageValidationCatalog.UsProfile,
            PackageValidationMode.Development, "legacy-history-validation"));
        Assert.Null(historyQueued.Run.Binding.StandardsSnapshotId);
        var (historyRun, historyReport) = await validation.CompleteAsync(historyQueued.Run, legacy);
        Assert.False(historyReport.IsReadyForFinalization);
        var historyPackage = validation.Package with { SequenceNumber = "0000", Sequence = validation.Package.Sequence with { SequenceNumber = "0000" } };
        var releaseStore = new RecordingReleaseStore(fixture.Root);
        var capture = new ReleaseCaptureService(validation.Store,
            new EctdReleaseCapturePlanner(new FixedPackageBuilder(historyPackage), new IchIndexXmlWriter(), Registry(), fixture.Provider), releaseStore);
        var finalization = new FinalizationService(finalizations, capture, new InMemoryWorkspaceRevisionStore(validation.Applications));
        var snapshot = await finalization.FinalizeAsync(new(validation.App.Id, "0000", 0, historyRun.Id,
            PackageValidationCatalog.UsProfile, "3.2.2", "3.3", historyRun.Binding.HistoryBaselineDigest, "legacy-history-release"));
        Assert.Equal(PackageValidationMode.Development, snapshot.Sequence.ValidationMode);
        Assert.Equal(historyRun.Binding.InputManifestDigest, snapshot.Sequence.ValidationInputManifestDigest);
        Assert.Equal(historyRun.ReportSha256, releaseStore.Manifest!.Validation.ReportSha256);

        var queued = await legacy.EnqueueAsync(new(validation.Input.Id, PackageValidationCatalog.UsProfile,
            PackageValidationMode.Development, "legacy-target-queued", History: [new(historical.Id, PackageValidationCatalog.UsProfile)]));
        Assert.Null(queued.Run.Binding.StandardsSnapshotId);
        using var archived = JsonDocument.Parse(queued.Run.RequestJson);
        var entry = Assert.Single(archived.RootElement.GetProperty("historyBaseline").GetProperty("entries").EnumerateArray());
        Assert.Equal("VerifiedForScope", entry.GetProperty("trustStatus").GetString());
        Assert.Equal(snapshot.Sequence.Id, entry.GetProperty("finalizedSequenceId").GetGuid());
        Assert.Equal(historyRun.Id, entry.GetProperty("validationRunId").GetGuid());
        var before = await legacy.InspectAsync(queued.Run);
        var upgraded = new PackageValidationService(validation.Store, validation.Reader, validation.Paths,
            new(new PdfPigPackageInspector()), timeProvider: new FixedClock(), finalizedHistory: resolver,
            snapshots: fixture.Service, applications: validation.Applications);
        var replay = await upgraded.InspectAsync(queued.Run);
        Assert.Equal(queued.Run.Binding, replay.Binding);
        Assert.Equal(before.Binding.HistoryBaselineDigest, replay.Binding.HistoryBaselineDigest);
        Assert.Equal(PackageValidationCatalog.SerializeReport(before), PackageValidationCatalog.SerializeReport(replay));
        Assert.False(replay.IsReadyForFinalization);
        await fixture.EvidenceAsync("legacy-verified-history", new { queuedRunId = queued.Run.Id,
            historyRunId = historyRun.Id, finalizedHistoryId = snapshot.Sequence.Id,
            queued.Run.Binding.HistoryBaselineDigest, snapshot.Sequence.ManifestDigest, mode = "Development" });
    }

    [Fact]
    public async Task ProviderOnlyAssetUpgradeMayBeArchivedButCannotCreateAContradictoryValidationRun()
    {
        var fixture = await SnapshotFixture.CreateAsync();
        var validation = await ValidationFixture.CreateAsync(fixture);
        var before = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        await fixture.ChangeAssetAsync();
        var contradictory = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        Assert.NotEqual(before.Id, contradictory.Id);
        var providerDtd = contradictory.Resources.Single(resource => resource.Key == "provider-ich-ectd-3-2-dtd");
        var inspectorDtd = contradictory.Resources.Single(resource => resource.Key == "xml-ich-dtd");
        Assert.NotEqual(providerDtd.Sha256, inspectorDtd.Sha256);
        Assert.Equal(contradictory.Id, (await fixture.Service.GetAsync(contradictory.Id)).Id);
        Assert.Equal(providerDtd.ReadBytes(), (await fixture.Service.OpenResourceAsync(contradictory.Id, providerDtd.Key)).Bytes);
        Assert.Throws<InvalidDataException>(() => PackageStandardsContext.FromSnapshot(contradictory));
        await Assert.ThrowsAsync<InvalidDataException>(() => validation.Service.EnqueueAsync(new(validation.Input.Id,
            PackageValidationCatalog.UsProfile, PackageValidationMode.Development, "contradictory-provider-assets", StandardsSnapshotId: contradictory.Id)));
        Assert.Empty((await validation.Store.ListRunsAsync(validation.App.Id, 0, 100)).Items);
        Assert.Equal(before.Id, (await fixture.Service.GetAsync(before.Id)).Id);
        Assert.Equal(before.Id, PackageStandardsContext.FromSnapshot(before).SnapshotId);
    }

    [Fact]
    public async Task SnapshotCapturePreservesAnImportedCustomRegionalPathThroughTheActualPackageBuilder()
    {
        const string regionalPath = "m1/us/backbones/source.xml";
        var fixture = await SnapshotFixture.CreateAsync();
        var validation = await ValidationFixture.CreateAsync(fixture, regionalPath);
        Assert.IsType<EctdPackageModelBuilder>(validation.ModelBuilder);
        Assert.Equal(regionalPath, validation.Package.BackboneXml.Regional.RelativePath);
        Assert.Equal("../../../util/dtd/us-regional-v3-3.dtd", validation.Package.BackboneXml.Regional.DtdSystemId);
        var snapshot = await fixture.Service.CaptureAsync(SnapshotFixture.Template);
        var queued = await validation.Service.EnqueueAsync(new(validation.Input.Id, PackageValidationCatalog.UsProfile,
            PackageValidationMode.Development, "custom-imported-regional-path", StandardsSnapshotId: snapshot.Id));
        var (run, report) = await validation.CompleteAsync(queued.Run);
        // This legacy builder maps the six-digit application number to applicant id.
        // Preserve its exact bytes and expose the new official defect; only typed
        // regional metadata can supply a distinct applicant identifier.
        Assert.Equal("FDA47-2044", Assert.Single(report.Coverage.SelectMany(rule => rule.Findings),
            finding => finding.CheckStatus == CheckStatus.Fail).RuleId);
        Assert.Equal(CheckStatus.Pass, Assert.Single(report.Coverage, rule => rule.RuleId == "FDA47-2036").CheckStatus);
        Assert.False(report.IsReadyForFinalization);
        fixture.Provider.RefuseReads = true;
        var files = new RecordingReleaseStore(fixture.Root);
        var capture = await new ReleaseCaptureService(validation.Store,
            new EctdReleaseCapturePlanner(validation.ModelBuilder, new IchIndexXmlWriter(), Registry(), fixture.Provider, snapshots: fixture.Service),
            files, snapshots: fixture.Service).CaptureAsync(new(validation.App.Id, "0001", validation.Package.WorkspaceRevision,
                run.Id, PackageValidationCatalog.UsProfile, "3.2.2", "3.3", run.Binding.HistoryBaselineDigest, "custom-path-release"));
        var original = await File.ReadAllBytesAsync(Path.Combine(validation.Input.SourcePath, regionalPath));
        Assert.Equal(original, files.Bytes["payload/0001/" + regionalPath]);
        Assert.Contains("../../../util/dtd/us-regional-v3-3.dtd", Encoding.UTF8.GetString(original), StringComparison.Ordinal);
        Assert.Contains(regionalPath, Encoding.UTF8.GetString(files.Bytes["payload/0001/index.xml"]), StringComparison.Ordinal);
        Assert.Equal(snapshot.Id, files.Manifest!.Validation.StandardsSnapshotId);
        Assert.Equal(run.ReportSha256, files.Manifest.Validation.ReportSha256);
        Assert.Equal(files.Manifest.Digest, capture.ManifestDigest);
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string Md5(byte[] bytes) => Convert.ToHexString(MD5.HashData(bytes)).ToLowerInvariant();
    private static RegionalBackboneWriterRegistry Registry() => new([new UsRegionalBackboneWriter(new UsRegionalXmlWriter())]);

    private sealed class ValidationFixture
    {
        public static DateOnly Today => new(2026, 11, 5);
        public static StandardsBusinessContext Context => new("nda", "national", "US", "en", "m3", "new");
        public required SubmissionApplication App { get; init; }
        public required InMemoryApplicationRepository Applications { get; init; }
        public required RegisteredPackageInput Input { get; init; }
        public required InMemoryPackageValidationStore Store { get; init; }
        public required PackageValidationService Service { get; init; }
        public required RetainedReader Reader { get; init; }
        public required RetainedPaths Paths { get; init; }
        public required EctdSequencePackage Package { get; init; }
        public required IEctdPackageModelBuilder ModelBuilder { get; init; }
        public static async Task<ValidationFixture> CreateAsync(SnapshotFixture snapshots, string? customRegionalPath = null)
        {
            var app = new SubmissionApplication("000001", "US", "Synthetic Sponsor", snapshots.Root, SnapshotFixture.Template);
            app.CreateSequence("0001", "original", "Snapshot integration").RevisePublishingMetadata(SequencePublishingMetadata.Create(
                "nda", "original-application", "application", "Snapshot integration", "Synthetic Sponsor", null,
                "Test Contact", "regulatory", "5550100", "business", "synthetic@example.test"));
            var apps = new InMemoryApplicationRepository();
            var profile = snapshots.Provider.GetProfile(SnapshotFixture.Template);
            var package = new EctdSequencePackage(app.Id, "000001", "0001", SnapshotFixture.Template, "3.2.2", "3.3", profile.BackboneXml!,
                new("000001", "Synthetic Sponsor", "US", SnapshotFixture.Template, "nda"),
                new("0001", "original-application", "application", "Snapshot integration", "Synthetic Sponsor", null),
                new(customRegionalPath is null ? "123456789" : "000001", "Synthetic Sponsor", "Snapshot integration", "Test Contact", "regulatory", "5550100", "business",
                    "synthetic@example.test", "nda", "original-application", "application", null), [], [], [], Nodes: []);
            if (customRegionalPath is not null) package = package with { BackboneXml = package.BackboneXml with
            { Regional = package.BackboneXml.Regional with { RelativePath = customRegionalPath, DtdSystemId = "../../../util/dtd/us-regional-v3-3.dtd" } } };
            var regional = Registry().Resolve("US").WriteRegionalBackbones(package);
            var index = new IchIndexXmlWriter().Write(EctdBackboneComposition.AttachRegionalFiles(package, regional));
            IEctdPackageModelBuilder builder;
            if (customRegionalPath is null)
            { await apps.AddAsync(app); builder = new FixedPackageBuilder(package); }
            else
            {
                var documents = new InMemoryDocumentRepository(); var placements = new InMemoryDocumentPlacementRepository();
                var revisions = new InMemoryWorkspaceRevisionStore(apps); var nodes = new InMemoryCtdNodeRepository(revisions, placements);
                var imports = new InMemoryApplicationImportStore(apps, documents, placements, nodes);
                await imports.SaveAsync(new(app, new CtdNodeGraph(app.Id, IchSectionDefinitions.Current), [], [], [],
                    regional.Select(file => new ImportedBackbone("0001", file.RelativePath, file.Content))
                        .Append(new("0001", "index.xml", index.XmlContent)).ToArray()));
                builder = new EctdPackageModelBuilder(apps, placements, documents, snapshots.Provider,
                    new DocumentStorageBoundary(new RetainedPaths(snapshots.Root)), nodes, revisions, imports);
                package = await builder.BuildAsync(new(app.Id, "0001"));
            }
            var directory = Path.Combine(snapshots.Root, "delivery", "0001"); Directory.CreateDirectory(directory);
            foreach (var file in regional.Append(new("index.xml", index.XmlContent)))
            {
                var path = Path.Combine(directory, file.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, file.Content);
            }
            foreach (var asset in profile.Assets)
            {
                var path = Path.Combine(directory, "util", "dtd", Path.GetFileName(asset.LocalRelativePath)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.Copy(Path.Combine(snapshots.Root, asset.LocalRelativePath), path);
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "index-md5.txt"), Md5(await File.ReadAllBytesAsync(Path.Combine(directory, "index.xml"))) + Environment.NewLine);
            var store = new InMemoryPackageValidationStore(); var reader = new RetainedReader();
            var paths = new RetainedPaths(snapshots.Root);
            var registry = new PackageValidationInputRegistry(store, reader, paths, Options.Create(new PackageValidationOptions()), new FixedClock());
            var registered = await registry.RegisterAsync(new(app.Id, directory, "0001"));
            var input = (await store.GetInputAsync(registered.Id))!;
            var service = new PackageValidationService(store, reader, paths, new(new PdfPigPackageInspector()),
                timeProvider: new FixedClock(), snapshots: snapshots.Service, applications: apps);
            return new() { App = app, Applications = apps, Input = input, Store = store, Service = service, Reader = reader, Paths = paths, Package = package, ModelBuilder = builder };
        }
        public async Task<(PackageValidationRun Run, ValidationReport Report)> CompleteAsync(PackageValidationRun run, PackageValidationService? service = null)
        {
            var lease = (await Store.ClaimAsync("snapshot-integration", FixedClock.Now.UtcDateTime, TimeSpan.FromMinutes(5)))!;
            Assert.Equal(run.Id, lease.Run.Id);
            var report = await (service ?? Service).InspectAsync(lease.Run);
            Assert.True(await Store.CompleteAsync(run.Id, lease.Token, lease.Owner, FixedClock.Now.UtcDateTime, report));
            return ((await Store.GetRunAsync(run.Id))!, report);
        }
        public async Task<RegisteredPackageInput> RegisterHistoryAsync(StandardsProfileSnapshot snapshot)
        {
            App.CreateSequence("0000", "original", "Historical snapshot"); await Applications.UpdateAsync(App);
            var package = Package with { SequenceNumber = "0000", Sequence = Package.Sequence with { SequenceNumber = "0000" } };
            var regional = Registry().Resolve("US").WriteRegionalBackbones(package);
            var index = new IchIndexXmlWriter().Write(EctdBackboneComposition.AttachRegionalFiles(package, regional));
            var directory = Path.Combine(Path.GetDirectoryName(Input.SourcePath)!, "0000"); Directory.CreateDirectory(directory);
            foreach (var file in regional.Append(new("index.xml", index.XmlContent)))
            {
                var path = Path.Combine(directory, file.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllTextAsync(path, file.Content);
            }
            foreach (var resource in snapshot.Resources.Where(resource => resource.Key.StartsWith("provider-", StringComparison.Ordinal)))
            {
                var path = Path.Combine(directory, "util", "dtd", Path.GetFileName(resource.RelativePath)); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, resource.ReadBytes());
            }
            await File.WriteAllTextAsync(Path.Combine(directory, "index-md5.txt"), Md5(await File.ReadAllBytesAsync(Path.Combine(directory, "index.xml"))) + Environment.NewLine);
            var registry = new PackageValidationInputRegistry(Store, Reader, Paths, Options.Create(new PackageValidationOptions()), new FixedClock());
            var registered = await registry.RegisterAsync(new(App.Id, directory, "0000"));
            return (await Store.GetInputAsync(registered.Id))!;
        }
    }
    private sealed class FixedClock : TimeProvider
    {
        public static DateTimeOffset Now => new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    private sealed class FixedPackageBuilder(EctdSequencePackage package) : IEctdPackageModelBuilder
    {
        public Task<EctdSequencePackage> BuildAsync(BuildEctdPackageRequest request, CancellationToken cancellationToken = default)
        { Assert.Equal(package.ApplicationId, request.ApplicationId); Assert.Equal(package.SequenceNumber, request.SequenceNumber); return Task.FromResult(package); }
    }
    private sealed class RetainedPaths(string root) : IWorkspacePathPolicy
    {
        public IReadOnlyCollection<string> GetAllowedRoots() => [root];
        public string EnsureAllowed(string path)
        { var full = Path.GetFullPath(path); if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(); return full; }
    }
    private sealed class RetainedReader : IPackageInputReader
    {
        public Task<ICapturedPackageInput> ReadAsync(PackageInputSelection selection, PackageReadLimits limits, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var files = Directory.EnumerateFiles(selection.Path, "*", SearchOption.AllDirectories).ToDictionary(
                path => selection.SequenceNumber + "/" + Path.GetRelativePath(selection.Path, path).Replace('\\', '/'), File.ReadAllBytes, StringComparer.Ordinal);
            return Task.FromResult<ICapturedPackageInput>(new RetainedInput(selection, limits, files));
        }
    }
    private sealed class RetainedInput(PackageInputSelection selection, PackageReadLimits limits, Dictionary<string, byte[]> bytes) : ICapturedPackageInput
    {
        public string SourceId => selection.SourceId;
        public PackageSourceKind SourceKind => PackageSourceKind.Directory;
        public string? ArchiveSha256 => null;
        public PackageReadLimits Limits => limits;
        public PackageInputManifest Manifest { get; } = new(selection.ApplicationId, selection.SequenceNumber!,
            bytes.Select(file => new PackageInputFile(file.Key, file.Value.Length, Sha(file.Value), Md5(file.Value))), [], [], []);
        public string InputDigest => CanonicalJson.Digest(JsonSerializer.SerializeToElement(new { schemaVersion = 1, manifestDigest = Manifest.Digest, archiveSha256 = (string?)null }));
        public Stream OpenRead(string logicalPath) => new MemoryStream(bytes[logicalPath], writable: false);
        public Task VerifyUnchangedAsync(CancellationToken cancellationToken = default)
        {
            foreach (var file in Manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (Sha(File.ReadAllBytes(Path.Combine(selection.Path, file.LogicalPath[5..]))) != file.Sha256) throw new PackageInputChangedException(SourceId);
            }
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    // Uses the production planner and capture service, retaining every byte for
    // inspection without invoking a production provider's physical cleanup path.
    private sealed class RecordingReleaseStore(string root) : IReleaseCaptureStore
    {
        public Dictionary<string, byte[]> Bytes { get; } = new(StringComparer.Ordinal);
        public ReleaseManifest? Manifest { get; private set; }
        public async Task<ReleaseCaptureStaging> SaveAsync(ReleaseCapturePlan plan, CancellationToken cancellationToken = default)
        {
            var path = Path.Combine(root, "retained-release-" + Guid.NewGuid().ToString("N")); var files = new List<ReleaseManifestFile>();
            foreach (var item in plan.Files)
            {
                var bytes = item.Content ?? await File.ReadAllBytesAsync(item.SourcePath!, cancellationToken);
                Assert.Equal(item.Sha256, Sha(bytes)); Assert.Equal(item.Md5, Md5(bytes)); Assert.Equal(item.Length, bytes.LongLength);
                Bytes.Add(item.RelativePath, bytes); var destination = Path.Combine(path, item.RelativePath); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using (var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write)) await output.WriteAsync(bytes, cancellationToken);
                files.Add(new(item.RelativePath, bytes.LongLength, Sha(bytes), Md5(bytes), item.Kind));
            }
            return new(plan.ApplicationId, plan.SequenceNumber, path, files);
        }
        public async Task CompleteAsync(ReleaseCaptureStaging staging, ReleaseManifest manifest, CancellationToken cancellationToken = default)
        { Manifest = manifest; await File.WriteAllBytesAsync(Path.Combine(staging.Directory, "manifest.json"), manifest.ToUtf8Json(), cancellationToken); }
        public Task<ReleaseCommitResult> CommitAsync(ReleaseCaptureStaging staging, string manifestDigest, CancellationToken cancellationToken = default)
        { Assert.Equal(Manifest!.Digest, manifestDigest); return Task.FromResult(new ReleaseCommitResult(staging.Directory, true)); }
        public Task DiscardAsync(ReleaseCaptureStaging staging, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DiscardCommittedAsync(Guid applicationId, string sequenceNumber, string manifestDigest, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ReleaseManifest?> ReadManifestAsync(Guid applicationId, string sequenceNumber, string manifestDigest, CancellationToken cancellationToken = default) => Task.FromResult(Manifest);
        public Task<ReleaseFileContent> OpenFileAsync(Guid applicationId, string sequenceNumber, string manifestDigest, string relativePath, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SnapshotFixture
    {
    private static readonly JsonSerializerOptions CachedJsonOptions1 = new JsonSerializerOptions { WriteIndented = true };

        public const string Template = "us-fda-ectd-3.2.2";
        public required string Root { get; init; }
        public required MutableProvider Provider { get; init; }
        public required InMemoryStandardsSnapshotStore Store { get; init; }
        public required StandardsSnapshotService Service { get; init; }
        public string IchPath => Path.Combine(Root, "reference", "dtd", "ich-ectd-3-2.dtd");
        public static async Task<SnapshotFixture> CreateAsync()
        {
            var root = Path.GetFullPath(".artifacts/p602-integration/" + Guid.NewGuid().ToString("N"));
            foreach (var asset in new FdaEctd322StandardsProfileProvider().GetProfile(Template).Assets)
            {
                var destination = Path.Combine(root, asset.LocalRelativePath); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                await using var source = File.OpenRead(Path.Combine(AppContext.BaseDirectory, asset.LocalRelativePath));
                await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
                await source.CopyToAsync(output);
            }
            var provider = new MutableProvider(root); var store = new InMemoryStandardsSnapshotStore();
            return new() { Root = root, Provider = provider, Store = store, Service = new(store, provider, new() { AssetRootPath = root }) };
        }
        public async Task UpgradeAsync()
        {
            await ChangeAssetAsync();
            Provider.GuideVersion = "synthetic-next-guide";
        }
        public Task ChangeAssetAsync() => File.AppendAllTextAsync(IchPath, "\n<!-- Synthetic fixture asset revision: old captured bytes must remain unchanged. -->\n");
        public Task EvidenceAsync(string name, object evidence) => File.WriteAllTextAsync(Path.Combine(Root, name + "-evidence.json"),
            JsonSerializer.Serialize(evidence, CachedJsonOptions1));
    }
    private sealed class MutableProvider(string root) : IStandardsProfileProvider
    {
        public string? GuideVersion { get; set; }
        public bool RefuseReads { get; set; }
        public StandardsProfile GetProfile(string templateKey)
        {
            if (RefuseReads) throw new InvalidOperationException("A historical snapshot read must not call the current provider.");
            var profile = new FdaEctd322StandardsProfileProvider(root).GetProfile(templateKey);
            return GuideVersion is null ? profile : profile with { TechnicalConformanceGuideVersion = GuideVersion };
        }
    }
}
