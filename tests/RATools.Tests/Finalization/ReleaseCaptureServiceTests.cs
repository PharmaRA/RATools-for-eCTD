using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RATools.Application.Finalization;
using RATools.Application.PackageValidation;
using RATools.Domain.Common;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Finalization;

// P4-02/P4-03 at the service boundary: the snapshot digest comes from the captured bytes,
// the archived report and the validated input - never from the request - and a workspace
// that changed after validation cannot be delivered as if it had been inspected.
public sealed class ReleaseCaptureServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CaptureDerivesTheDigestFromBytesAndReportFactsNotFromTheRequest()
    {
        var fixture = new Fixture();
        var run = await fixture.CompletedRunAsync();
        var request = Fixture.Request(run);
        var capture = await new ReleaseCaptureService(fixture.ValidationStore,
            new FakePlanner(Fixture.Preparation(run, validatedSha256: fixture.DocumentSha256)), fixture.Store)
            .CaptureAsync(request);

        // The digest is the manifest's own canonical digest, not anything the request said.
        Assert.Equal(fixture.Store.Completed!.Digest, capture.ManifestDigest);
        Assert.Equal(run.Id, capture.ValidationRunId);
        Assert.Equal(run.ReportSha256, capture.ReportSha256);
        Assert.Equal(run.Binding.ValidationMode, capture.ValidationMode);
        // The manifest that was written lists the measured files plus the archived report.
        var staged = fixture.Store.Completed!;
        Assert.Contains(staged.Files, file => file.RelativePath == "validation-report.json" &&
            file.Sha256 == run.ReportSha256 && file.Kind == ReleaseFileKind.ValidationReport);
        Assert.Contains(staged.Files, file => file.RelativePath == "payload/0001/index.xml");
    }

    [Fact]
    public async Task AWorkspaceDocumentThatDiffersFromTheValidatedInputIsRefused()
    {
        var fixture = new Fixture();
        var run = await fixture.CompletedRunAsync();
        // The planner reports a document digest that is not the one the run captured,
        // exactly what happens when the workspace was edited after validation.
        var service = new ReleaseCaptureService(fixture.ValidationStore,
            new FakePlanner(Fixture.Preparation(run, validatedSha256: new string('9', 64))), fixture.Store);
        var error = await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => service.CaptureAsync(Fixture.Request(run)));
        Assert.Contains("workspace changed after validation", error.Message, StringComparison.Ordinal);
        // Nothing was staged: the refusal happens before the first byte is copied.
        Assert.Null(fixture.Store.Completed);
    }

    [Fact]
    public async Task AnUnreadyFormalReportIsRefusedBeforeAnyCapture()
    {
        var fixture = new Fixture();
        var run = await fixture.CompletedRunAsync(PackageValidationMode.Formal);
        var service = new ReleaseCaptureService(fixture.ValidationStore,
            new FakePlanner(Fixture.Preparation(run, fixture.DocumentSha256)), fixture.Store);
        var error = await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => service.CaptureAsync(Fixture.Request(run)));
        Assert.Contains("ready for finalization", error.Message, StringComparison.Ordinal);
        Assert.Null(fixture.Store.Completed);
    }

    [Fact]
    public async Task ARequestNamingAnotherBaselineIsRefusedBeforeAnyCapture()
    {
        var fixture = new Fixture();
        var run = await fixture.CompletedRunAsync();
        var service = new ReleaseCaptureService(fixture.ValidationStore,
            new FakePlanner(Fixture.Preparation(run, fixture.DocumentSha256)), fixture.Store);
        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() =>
            service.CaptureAsync(Fixture.Request(run) with { HistoryBaselineDigest = new string('d', 64) }));
        Assert.Null(fixture.Store.Completed);
    }

    [Theory]
    [InlineData("payload/0001/index.xml")]
    [InlineData("payload/0001/m1/us/us-regional.xml")]
    [InlineData("payload/0001/index-md5.txt")]
    [InlineData("payload/0001/util/dtd/ich-ectd-3-2.dtd")]
    public async Task OldRunCannotAuthorizeChangedGeneratedOrStandardsBytesAtANewRevision(string path)
    {
        var fixture = new Fixture();
        var run = await fixture.CompletedRunAsync();
        var original = Fixture.Preparation(run, fixture.DocumentSha256);
        var changed = original with
        {
            Capture = original.Capture with
            {
                Files = original.Capture.Files.Select(file => file.RelativePath == path
                    ? ReleaseCaptureFile.Generated(path, Encoding.UTF8.GetBytes("changed after validation")) : file).ToArray()
            }
        };
        var service = new ReleaseCaptureService(fixture.ValidationStore, new FakePlanner(changed), fixture.Store);

        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() =>
            service.CaptureAsync(Fixture.Request(run) with { ExpectedRevision = 5 }));

        Assert.Null(fixture.Store.Staging);
    }

    [Fact]
    public async Task OldRunCannotAuthorizeADeletedPlacementEvenWhenRemainingDocumentsMatch()
    {
        var fixture = new Fixture();
        var run = await fixture.CompletedRunAsync();
        var original = Fixture.Preparation(run, fixture.DocumentSha256);
        var changed = original with
        {
            Capture = original.Capture with
            {
                Files = original.Capture.Files.Where(file => !file.RelativePath.EndsWith("/intro.pdf", StringComparison.Ordinal)).ToArray()
            },
            ValidatedDocuments = new Dictionary<string, string>()
        };
        var service = new ReleaseCaptureService(fixture.ValidationStore, new FakePlanner(changed), fixture.Store);

        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() =>
            service.CaptureAsync(Fixture.Request(run) with { ExpectedRevision = 5 }));

        Assert.Null(fixture.Store.Staging);
    }

    [Fact]
    public async Task AnUnvalidatedAdditionalPayloadIsRefused()
    {
        var fixture = new Fixture();
        var run = await fixture.CompletedRunAsync();
        var original = Fixture.Preparation(run, fixture.DocumentSha256);
        var changed = original with
        {
            Capture = original.Capture with
            {
                Files = original.Capture.Files.Append(ReleaseCaptureFile.Generated(
                    "payload/0001/extra.pdf", Encoding.UTF8.GetBytes("unvalidated"))).ToArray()
            }
        };
        var service = new ReleaseCaptureService(fixture.ValidationStore, new FakePlanner(changed), fixture.Store);

        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => service.CaptureAsync(Fixture.Request(run)));

        Assert.Null(fixture.Store.Staging);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AMatchingSha256DoesNotAuthorizeDifferentLengthOrMd5(bool changeLength)
    {
        var fixture = new Fixture();
        var run = await fixture.CompletedRunAsync();
        var original = Fixture.Preparation(run, fixture.DocumentSha256);
        var changed = original with
        {
            Capture = original.Capture with
            {
                Files = original.Capture.Files.Select((file, index) => index != 0 ? file : changeLength
                    ? file with { Length = file.Length + 1 } : file with { Md5 = new string('0', 32) }).ToArray()
            }
        };
        var service = new ReleaseCaptureService(fixture.ValidationStore, new FakePlanner(changed), fixture.Store);

        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => service.CaptureAsync(Fixture.Request(run)));

        Assert.Null(fixture.Store.Staging);
    }

    [Theory]
    [InlineData("inventory")]
    [InlineData("application")]
    [InlineData("capture")]
    [InlineData("run")]
    public async Task AnArchivedInputThatNoLongerMatchesItsSealedIdentityIsRefused(string mutation)
    {
        var fixture = new Fixture();
        var run = await fixture.CompletedRunAsync(mutation: mutation);
        var service = new ReleaseCaptureService(fixture.ValidationStore,
            new FakePlanner(Fixture.Preparation(run, fixture.DocumentSha256)), fixture.Store);

        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => service.CaptureAsync(Fixture.Request(run)));

        Assert.Null(fixture.Store.Staging);
    }

    [Fact]
    public async Task DirectoryAndZipComparisonRemainBoundToTheirCombinedInputIdentity()
    {
        var fixture = new Fixture();
        var run = await fixture.CompletedRunAsync(withComparison: true);
        var service = new ReleaseCaptureService(fixture.ValidationStore,
            new FakePlanner(Fixture.Preparation(run, fixture.DocumentSha256)), fixture.Store);

        var capture = await service.CaptureAsync(Fixture.Request(run));

        Assert.Equal(run.Binding.InputManifestDigest, capture.ValidationInputManifestDigest);
        Assert.NotNull(fixture.Store.Completed);
    }

    [Fact]
    public async Task ChangedComparisonIdentityCannotReuseTheCompletedRun()
    {
        var fixture = new Fixture();
        var run = await fixture.CompletedRunAsync(mutation: "comparison", withComparison: true);
        var service = new ReleaseCaptureService(fixture.ValidationStore,
            new FakePlanner(Fixture.Preparation(run, fixture.DocumentSha256)), fixture.Store);

        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => service.CaptureAsync(Fixture.Request(run)));

        Assert.Null(fixture.Store.Staging);
    }

    private static string Digest(string seed) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();

    private sealed class Fixture
    {
        public string DocumentSha256 { get; } = Digest("document");
        public InMemoryPackageValidationStore ValidationStore { get; } = new();
        public RecordingCaptureStore Store { get; } = new();

        public async Task<PackageValidationRun> CompletedRunAsync(
            PackageValidationMode mode = PackageValidationMode.Development, string? mutation = null, bool withComparison = false)
        {
            var baseline = Baseline();
            var limits = new PackageReadLimits();
            var manifest = new PackageInputManifest(ApplicationId, "0001", PayloadFiles().Select(file =>
                new PackageInputFile(file.RelativePath["payload/".Length..], file.Length, file.Sha256, file.Md5)), [], [], []);
            var inputDigest = CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
            {
                schemaVersion = 1, manifestDigest = manifest.Digest, archiveSha256 = (string?)null
            }));
            var manifestJson = JsonSerializer.SerializeToNode(manifest, SerializerOptions)!.AsObject();
            manifestJson["capture"] = JsonSerializer.SerializeToNode(new
            {
                schemaVersion = 1, inputDigest, archiveSha256 = (string?)null,
                sourceKind = PackageSourceKind.Directory, limitsDigest = limits.Digest()
            }, SerializerOptions);
            if (mutation == "inventory") manifestJson["files"]![0]!["length"] = 1234L;
            if (mutation == "application") manifestJson["applicationId"] = Guid.NewGuid().ToString("D");
            if (mutation == "capture") manifestJson["capture"]!["inputDigest"] = new string('a', 64);
            Guid? comparisonId = null;
            string? comparisonDigest = null;
            if (withComparison)
            {
                comparisonId = Guid.NewGuid();
                var archiveSha256 = Digest("zip bytes");
                comparisonDigest = CanonicalJson.Digest(JsonSerializer.SerializeToElement(new
                {
                    schemaVersion = 1, manifestDigest = manifest.Digest, archiveSha256
                }));
                var comparisonJson = JsonSerializer.SerializeToNode(manifest, SerializerOptions)!.AsObject();
                comparisonJson["capture"] = JsonSerializer.SerializeToNode(new
                {
                    schemaVersion = 1, inputDigest = comparisonDigest,
                    archiveSha256 = mutation == "comparison" ? Digest("changed zip") : archiveSha256,
                    sourceKind = PackageSourceKind.Zip, limitsDigest = limits.Digest()
                }, SerializerOptions);
                await ValidationStore.RegisterInputAsync(new(comparisonId.Value, ApplicationId, "0001", "/fixture.zip", null,
                    PackageSourceKind.Zip, comparisonDigest, comparisonJson.ToJsonString(SerializerOptions), limits, Now, false));
            }
            // The archived baseline carries its own digest; the run binding must name that
            // exact digest, exactly as the real validation service records it.
            var binding = new ValidationBinding(mutation == "run" ? Digest("different input") : PackageDeliveryInput.ComputeDigest(inputDigest, comparisonDigest), Digest("baseline"),
                PackageValidationCatalog.UsProfile, PackageValidationCatalog.Current.Digest, "package-delivery-v3", mode,
                limits.Digest());
            var stored = new
            {
                schemaVersion = 1, inputId = InputId, comparisonInputId = comparisonId,
                history = Array.Empty<object>(), ruleCatalog = PackageValidationCatalog.ExportSnapshot(),
                historyBaseline = baseline, readLimits = limits
            };
            var requestJson = JsonSerializer.Serialize(stored, SerializerOptions);
            var run = new PackageValidationRun(ApplicationId, "0001", binding, requestJson,
                Guid.NewGuid().ToString("N"), Now);
            await ValidationStore.EnqueueAsync(run);
            var lease = (await ValidationStore.ClaimAsync("worker", Now, TimeSpan.FromMinutes(1)))!;
            var report = ValidationReport.Create(PackageValidationCatalog.Current, binding, ValidationRunStatus.Completed,
                [new RuleCheckResult("FILE-HREF", CheckStatus.NotEvaluated, "Not executed in this fixture.")]);
            Assert.True(await ValidationStore.CompleteAsync(run.Id, lease.Token, lease.Owner, Now, report));
            var stored1 = (await ValidationStore.GetRunAsync(run.Id))!;
            // The input registration must exist for the service to verify the captured bytes.
            await ValidationStore.RegisterInputAsync(new RegisteredPackageInput(InputId, ApplicationId, "0001",
                "/fixture/path", null, PackageSourceKind.Directory, inputDigest, manifestJson.ToJsonString(SerializerOptions), limits,
                Now, IsOwnedUpload: false));
            return stored1;
        }

        private static readonly Guid InputId = Guid.NewGuid();
        private static readonly Guid ApplicationId = Guid.NewGuid();

        private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
        { Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() } };

        private static JsonElement Baseline() => JsonSerializer.SerializeToElement(new
        {
            applicationId = ApplicationId.ToString("D"), targetSequence = "0001", digest = Digest("baseline"),
            entries = Array.Empty<object>()
        });

        private static ReleaseCaptureFile[] PayloadFiles() =>
        [
            ReleaseCaptureFile.Generated("payload/0001/index.xml", Encoding.UTF8.GetBytes("<ectd />")),
            ReleaseCaptureFile.Generated("payload/0001/m1/us/us-regional.xml", Encoding.UTF8.GetBytes("<regional />")),
            ReleaseCaptureFile.Generated("payload/0001/index-md5.txt", Encoding.UTF8.GetBytes("md5  index.xml")),
            ReleaseCaptureFile.Generated("payload/0001/util/dtd/ich-ectd-3-2.dtd", Encoding.UTF8.GetBytes("pinned dtd")),
            ReleaseCaptureFile.Generated("payload/0001/m2/intro.pdf", Encoding.UTF8.GetBytes("document"))
        ];

        public static ReleaseCapturePreparation Preparation(PackageValidationRun run, string validatedSha256)
        {
            // The service appends the archived report itself, so the planner only supplies
            // the deliverable files it derived from the workspace.
            var files = PayloadFiles().Select(file => file.RelativePath.EndsWith("/intro.pdf", StringComparison.Ordinal)
                ? file with { Sha256 = validatedSha256 } : file).ToArray();
            var baseline = Baseline();
            return new ReleaseCapturePreparation(new ReleaseCapturePlan(ApplicationId, "0001", files), [],
                new ReleaseValidationReference(run.ReportSha256!, run.Binding.ValidationMode.ToString(),
                    run.Binding.ProfileSnapshotId, "3.2.2", "3.3", run.Binding.RulesDigest, run.Binding.EngineVersion, null),
                baseline, new Dictionary<string, string> { ["0001/m2/intro.pdf"] = validatedSha256 }, [], [], []);
        }

        public static FinalizeSequenceRequest Request(PackageValidationRun run) =>
            new(run.ApplicationId, run.SequenceNumber, 4, run.Id, run.Binding.ProfileSnapshotId, "3.2.2", "3.3",
                run.Binding.HistoryBaselineDigest, Guid.NewGuid().ToString("N"));
    }

    // The planner is replaced so this test isolates the service's verification; the store
    // records what would be written and computes the digest the manifest would have.
    private sealed class FakePlanner(ReleaseCapturePreparation preparation) : IReleaseCapturePlanner
    {
        public Task<ReleaseCapturePreparation> PlanAsync(FinalizeSequenceRequest request, PackageValidationRun run,
            StoredPackageValidationRequest storedRequest, CancellationToken cancellationToken = default) =>
            Task.FromResult(preparation);
    }

    private sealed class RecordingCaptureStore : IReleaseCaptureStore
    {
        public ReleaseManifest? Completed { get; private set; }
        public ReleaseCaptureStaging? Staging { get; private set; }

        public Task<ReleaseCaptureStaging> SaveAsync(ReleaseCapturePlan plan, CancellationToken cancellationToken = default)
        {
            Staging = new ReleaseCaptureStaging(plan.ApplicationId, plan.SequenceNumber, "/fake/release",
                plan.Files.Select(file => new ReleaseManifestFile(file.RelativePath, file.Length, file.Sha256, file.Md5, file.Kind)).ToArray());
            return Task.FromResult(Staging);
        }

        public Task CompleteAsync(ReleaseCaptureStaging staging, ReleaseManifest manifest,
            CancellationToken cancellationToken = default)
        {
            Completed = manifest;
            return Task.CompletedTask;
        }

        public Task<ReleaseCommitResult> CommitAsync(ReleaseCaptureStaging staging, string manifestDigest,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ReleaseCommitResult("/fake/release", Created: true));

        public Task DiscardAsync(ReleaseCaptureStaging staging, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
        public Task DiscardCommittedAsync(Guid applicationId, string sequenceNumber, string manifestDigest,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<ReleaseManifest?> ReadManifestAsync(Guid applicationId, string sequenceNumber, string manifestDigest,
            CancellationToken cancellationToken = default) => Task.FromResult<ReleaseManifest?>(null);
        public Task<ReleaseFileContent> OpenFileAsync(Guid applicationId, string sequenceNumber, string manifestDigest,
            string relativePath, CancellationToken cancellationToken = default) =>
            throw new ReleaseCaptureMissingException(manifestDigest);
    }
}
