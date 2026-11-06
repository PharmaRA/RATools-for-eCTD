using RATools.Application.Abstractions.Persistence;
using RATools.Application.Finalization;
using RATools.Application.PackageValidation;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Finalization;

// The service never trusts a request-supplied manifest digest: the capture provider owns
// the bytes and the archived report facts, and the service records exactly those.
public sealed class FinalizationServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task FinalizeRecordsTheCapturedManifestAndArchivedRunFacts()
    {
        var fixture = new Fixture();
        var capture = fixture.Capture(PackageValidationMode.Development, ready: false);
        var result = await fixture.Service.FinalizeAsync(fixture.Request(capture));
        Assert.True(result.Created);
        Assert.Equal(capture.ManifestDigest, result.Sequence.ManifestDigest);
        Assert.Equal(capture.Run.Id, result.Sequence.ValidationRunId);
        Assert.Equal(capture.Run.Binding.InputManifestDigest, result.Sequence.ValidationInputManifestDigest);
        Assert.Equal(capture.ReportSha256, result.Sequence.ValidationReportSha256);
        Assert.Equal(capture.Run.Binding.HistoryBaselineDigest, result.Sequence.HistoryBaselineDigest);
        Assert.Equal(PackageValidationMode.Development, result.Sequence.ValidationMode);
        Assert.False(result.Sequence.IsReadyForFinalization);
        Assert.Contains(result.Sequence.BlockingReasons, reason => reason.StartsWith("DevelopmentMode", StringComparison.Ordinal));
        Assert.Equal(0, capture.DiscardCount);
        // A development snapshot is an archive of what was validated, not authorization.
        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() =>
            fixture.Service.RegisterDeliveryAsync(result.Sequence.Id, Digest("package"), "gateway", null, null, "operator"));
    }

    [Fact]
    public async Task FinalizeRejectsABaselineOrProfileTheRunDidNotValidate()
    {
        var fixture = new Fixture();
        var capture = fixture.Capture(PackageValidationMode.Development, ready: false);
        var otherBaseline = fixture.Request(capture) with { HistoryBaselineDigest = new string('e', 64) };
        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => fixture.Service.FinalizeAsync(otherBaseline));
        var otherProfile = fixture.Request(capture) with
        {
            StandardsProfileSnapshotId = PackageValidationCatalog.IchProfile
        };
        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => fixture.Service.FinalizeAsync(otherProfile));
        // A sequence that does not exist on the application is not a finalization target.
        var otherSequence = fixture.Request(capture) with { SequenceNumber = "0002" };
        await Assert.ThrowsAsync<FinalizationNotFoundException>(() => fixture.Service.FinalizeAsync(otherSequence));
        Assert.Empty(await fixture.FinalizationStore.ListBySequenceAsync(capture.Run.ApplicationId, capture.Run.SequenceNumber));
    }

    [Fact]
    public async Task FinalizeRejectsAChangedWorkspaceRevisionWithoutCapturing()
    {
        var fixture = new Fixture();
        var capture = fixture.Capture(PackageValidationMode.Development, ready: false);
        var stale = fixture.Request(capture);
        fixture.AdvanceRevision();
        await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => fixture.Service.FinalizeAsync(stale));
        // The revision is checked before any byte is copied.
        Assert.Empty(await fixture.FinalizationStore.ListBySequenceAsync(capture.Run.ApplicationId, capture.Run.SequenceNumber));
        Assert.Equal(0, fixture.CaptureAttempts);
    }

    [Fact]
    public async Task AnEditDuringCaptureIsDetectedAtRegistrationAndTheCaptureIsDiscarded()
    {
        var fixture = new Fixture();
        var capture = fixture.Capture(PackageValidationMode.Development, ready: false);
        // The operator's edit lands while the capture is copying bytes; registration then
        // sees a newer revision and refuses to finalize mixed content.
        fixture.OnCapture = fixture.AdvanceRevision;
        var request = fixture.Request(capture);
        await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() => fixture.Service.FinalizeAsync(request));
        // No snapshot was committed, so the directory this capture created is removed.
        Assert.Equal(1, capture.DiscardCount);
        Assert.True(capture.RemovedPublishedDirectory);
        Assert.Empty(await fixture.FinalizationStore.ListBySequenceAsync(capture.Run.ApplicationId, capture.Run.SequenceNumber));
    }

    [Fact]
    public async Task FormalFinalizationRefusesAnUnreadyReportBeforeAnyCapture()
    {
        var fixture = new Fixture();
        var capture = fixture.Capture(PackageValidationMode.Formal, ready: false, formalRefused: true);
        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => fixture.Service.FinalizeAsync(fixture.Request(capture)));
        Assert.Empty(await fixture.FinalizationStore.ListBySequenceAsync(capture.Run.ApplicationId, capture.Run.SequenceNumber));
        // The provider refuses the unready formal run before writing anything, so there is
        // no captured directory to remove.
        Assert.Equal(0, capture.DiscardCount);
    }

    [Fact]
    public async Task AFailedRegistrationNeverRemovesAReleaseASnapshotReferences()
    {
        var fixture = new Fixture();
        var capture = fixture.Capture(PackageValidationMode.Development, ready: false);
        // A snapshot with the same content already exists, so registration refuses the
        // capture after it was published. That release is committed evidence: the capture
        // must stop at discarding its own staging, never the shared directory.
        await fixture.FinalizationStore.RegisterAsync(FinalizedSequence.Create(capture.Run.ApplicationId, "0001",
            fixture.Application.Revision, capture.ManifestDigest, PackageValidationCatalog.UsProfile, "3.2.2", "3.3",
            capture.Run.Binding.HistoryBaselineDigest, capture.Run.Id, capture.Run.Binding.InputManifestDigest,
            capture.ReportSha256, PackageValidationMode.Development, false, [], FinalizationService.ToolVersion,
            "existing", DateTime.UtcNow));
        await Assert.ThrowsAsync<FinalizationSnapshotExistsException>(() =>
            fixture.Service.FinalizeAsync(fixture.Request(capture)));
        Assert.Equal(1, capture.DiscardCount);
        Assert.False(capture.RemovedPublishedDirectory);
    }

    [Fact]
    public async Task IdempotentReplayReturnsTheCommittedSnapshotAndChangedRequestsConflict()
    {
        var fixture = new Fixture();
        var capture = fixture.Capture(PackageValidationMode.Development, ready: false);
        var request = fixture.Request(capture) with { IdempotencyKey = "finalize-1" };
        var first = await fixture.Service.FinalizeAsync(request);
        // A replay returns the committed snapshot without capturing again.
        var replay = await fixture.Service.FinalizeAsync(request);
        Assert.True(first.Created);
        Assert.False(replay.Created);
        Assert.Equal(first.Sequence.Id, replay.Sequence.Id);
        Assert.Equal(1, fixture.CaptureAttempts);
        // The same captured content cannot be finalized twice under a new key.
        await Assert.ThrowsAsync<FinalizationSnapshotExistsException>(() => fixture.Service.FinalizeAsync(
            request with { IdempotencyKey = "finalize-2" }));
        // A key replay with a different request must not silently reuse earlier content.
        // The conflict is decided before capturing, so no bytes are written or removed.
        var attempts = fixture.CaptureAttempts;
        var changed = fixture.Capture(PackageValidationMode.Development, ready: false);
        await Assert.ThrowsAsync<FinalizationIdempotencyConflictException>(() => fixture.Service.FinalizeAsync(
            fixture.Request(changed) with { IdempotencyKey = "finalize-1" }));
        Assert.Equal(attempts, fixture.CaptureAttempts);
        Assert.Equal(0, changed.DiscardCount);
    }

    [Fact]
    public async Task DeliveryAndReceiptsAreAppendOnlyAndSurviveReload()
    {
        var fixture = new Fixture();
        // A ready formal snapshot can only be produced once a qualified profile exists.
        // This fixture registers one directly to exercise the append-only delivery
        // contract; the service itself only creates snapshots from a captured report.
        var snapshot = FinalizedSequence.Create(fixture.Application.Id, "0001", fixture.Application.Revision,
            Digest("manifest"), PackageValidationCatalog.UsProfile, "3.2.2", "3.3", Digest("history"), Guid.NewGuid(),
            Digest("input"), Digest("report"), PackageValidationMode.Formal, isReadyForFinalization: true, [],
            FinalizationService.ToolVersion, "ready-snapshot", DateTime.UtcNow);
        await fixture.FinalizationStore.RegisterAsync(snapshot);
        var delivery = await fixture.Service.RegisterDeliveryAsync(snapshot.Id, Digest("package"), "gateway",
            "EXT-1", "Exported and handed to the gateway.", "operator");
        var transmission = await fixture.Service.RegisterReceiptAsync(delivery.Id, DeliveryReceiptKind.Transmission,
            "Accepted", "<ack />", "EXT-1", "operator");
        await fixture.Service.RegisterReceiptAsync(delivery.Id, DeliveryReceiptKind.BusinessOutcome,
            "Pending", "<pending />", "EXT-1", "operator");
        Assert.Equal(2, (await fixture.FinalizationStore.ListReceiptsAsync(delivery.Id)).Count);
        var deliveries = await fixture.FinalizationStore.ListDeliveriesAsync(snapshot.Id);
        Assert.Equal(delivery.Digest, Assert.Single(deliveries).Digest);
        Assert.Equal(DeliveryReceiptKind.Transmission, transmission.Kind);
        Assert.NotEmpty(await fixture.Service.ListAsync(snapshot.ApplicationId, snapshot.SequenceNumber));
        // Appends never rewrite the snapshot identity.
        var reloaded = await fixture.Service.GetAsync(snapshot.Id);
        Assert.Equal(snapshot.RequestDigest, reloaded.RequestDigest);
    }

    [Fact]
    public async Task DeliveryRefusesAnArbitraryDigestEvenForAReadyFormalSnapshot()
    {
        var fixture = new Fixture();
        var snapshot = FinalizedSequence.Create(fixture.Application.Id, "0001", fixture.Application.Revision,
            Digest("manifest"), PackageValidationCatalog.UsProfile, "3.2.2", "3.3", Digest("history"), Guid.NewGuid(),
            Digest("input"), Digest("report"), PackageValidationMode.Formal, true, [],
            FinalizationService.ToolVersion, "ready-delivery-binding", DateTime.UtcNow);
        await fixture.FinalizationStore.RegisterAsync(snapshot);

        await Assert.ThrowsAsync<FinalizationNotAuthorizedException>(() => fixture.Service.RegisterDeliveryAsync(
            snapshot.Id, Digest("arbitrary unrelated zip"), "gateway", null, null, "operator"));

        Assert.Empty(await fixture.FinalizationStore.ListDeliveriesAsync(snapshot.Id));
    }

    private static string Digest(string seed) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed))).ToLowerInvariant();

    private sealed class Fixture
    {
        private readonly Queue<FakeCapture> _pending = new();
        private FakeCapture? _current;
        public InMemoryFinalizationStore FinalizationStore { get; } = new();
        public ApplicationRevision Application { get; } = new();
        public int CaptureAttempts { get; private set; }
        // Runs inside CaptureAsync to simulate work landing while the capture is copying.
        public Action? OnCapture { get; set; }
        public FinalizationService Service { get; }

        public Fixture() => Service = new FinalizationService(FinalizationStore, new FakeProvider(this), Application);

        public void AdvanceRevision() => Application.AdvanceRevision();

        // Builds a completed archived report, then queues the capture that reports those
        // exact run facts, so the service's own contract is isolated from package capture.
        public FakeCapture Capture(PackageValidationMode mode, bool ready, bool formalRefused = false)
        {
            var binding = new ValidationBinding(Digest("input"), Digest("history"), PackageValidationCatalog.UsProfile,
                PackageValidationCatalog.Current.Digest, "package-delivery-v3", mode, new PackageReadLimits().Digest());
            var report = ValidationReport.Create(PackageValidationCatalog.Current, binding, ValidationRunStatus.Completed,
                [new RuleCheckResult("FILE-HREF", ready ? CheckStatus.Pass : CheckStatus.NotEvaluated,
                    ready ? "References exist." : "Not executed.")]);
            var run = new PackageValidationRun(Application.Id, "0001", binding, "{\"inputId\":\"fixture\"}",
                Guid.NewGuid().ToString("N"), Now);
            var capture = new FakeCapture(run, Digest("manifest-" + CaptureAttempts + "-" + _pending.Count),
                Digest("report-" + _pending.Count), report.IsReadyForFinalization, report.BlockingReasons,
                formalRefused && mode == PackageValidationMode.Formal);
            _pending.Enqueue(capture);
            return capture;
        }

        public FinalizeSequenceRequest Request(FakeCapture capture) =>
            new(capture.Run.ApplicationId, capture.Run.SequenceNumber, Application.Revision, capture.Run.Id,
                capture.Run.Binding.ProfileSnapshotId, "3.2.2", "3.3", capture.Run.Binding.HistoryBaselineDigest,
                Guid.NewGuid().ToString("N"));

        // The provider resolves the capture that belongs to the requested run. An unknown
        // run reports no capture, exactly like the real store lookup returning nothing.
        public FakeCapture? Find(Guid runId)
        {
            CaptureAttempts++;
            OnCapture?.Invoke();
            foreach (var capture in _pending)
            {
                if (capture.Run.Id != runId) continue;
                _current = capture;
                return capture;
            }
            return _current is not null && _current.Run.Id == runId ? _current : null;
        }
    }

    // The capture provider is the byte authority. This fake states the run facts the
    // service must record and the digest the capture derived from the bytes.
    private sealed class FakeCapture(PackageValidationRun run, string manifestDigest, string reportSha256,
        bool reportReady, IReadOnlyList<string> blockingReasons, bool refused)
    {
        public PackageValidationRun Run { get; } = run;
        public string ManifestDigest { get; } = manifestDigest;
        public string ReportSha256 { get; } = reportSha256;
        public bool ReportReady { get; } = reportReady;
        public IReadOnlyList<string> BlockingReasons { get; } = blockingReasons;
        public bool Refused { get; } = refused;
        public int DiscardCount { get; private set; }
        // Records whether the caller let the capture remove a published release directory.
        public bool? RemovedPublishedDirectory { get; private set; }
        public void CountDiscard(bool removePublishedDirectory)
        {
            DiscardCount++;
            RemovedPublishedDirectory = removePublishedDirectory;
        }
    }

    // Mimics the real provider's refusals: an unknown run, a request that names a
    // different profile or baseline than the run bound, and an unready formal report are
    // all refused before anything is captured.
    private sealed class FakeProvider(Fixture fixture) : IFinalizationCaptureProvider
    {
        public Task<IFinalizationCapture> CaptureAsync(FinalizeSequenceRequest request,
            CancellationToken cancellationToken = default)
        {
            var capture = fixture.Find(request.PackageValidationRunId)
                ?? throw new FinalizationNotAuthorizedException("The referenced package validation run does not exist.");
            if (!string.Equals(capture.Run.Binding.ProfileSnapshotId, request.StandardsProfileSnapshotId.Trim(),
                    StringComparison.Ordinal) ||
                !string.Equals(capture.Run.Binding.HistoryBaselineDigest, request.HistoryBaselineDigest, StringComparison.Ordinal))
                throw new FinalizationNotAuthorizedException("The requested evidence is not the one this run validated.");
            if (capture.Refused) throw new FinalizationNotAuthorizedException("A formal finalization requires a ready report.");
            return Task.FromResult<IFinalizationCapture>(new Capture(capture));
        }

        // These tests never read a committed release; the store's own round trip is
        // covered by ReleaseCaptureTests.
        public Task<ReleaseManifest> ReadManifestAsync(FinalizedSequence snapshot,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("This fake does not store captured releases.");

        public Task<ReleaseFileContent> OpenFileAsync(FinalizedSequence snapshot, string relativePath,
            CancellationToken cancellationToken = default) => relativePath == "delivery-package.zip"
            ? Task.FromResult(new ReleaseFileContent("/fixture/delivery-package.zip", relativePath, 123,
                Digest("package"), "application/zip"))
            : throw new NotSupportedException("This fake does not store captured releases.");
    }

    private sealed class Capture(FakeCapture facts) : IFinalizationCapture
    {
        public string ManifestDigest => facts.ManifestDigest;
        public Guid ValidationRunId => facts.Run.Id;
        public string ValidationInputManifestDigest => facts.Run.Binding.InputManifestDigest;
        public string ReportSha256 => facts.ReportSha256;
        public PackageValidationMode ValidationMode => facts.Run.Binding.ValidationMode;
        public bool ReportReady => facts.ReportReady;
        public IReadOnlyList<string> BlockingReasons => facts.BlockingReasons;
        public Task PublishAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task DiscardAsync(bool removePublishedDirectory)
        {
            facts.CountDiscard(removePublishedDirectory);
            return Task.CompletedTask;
        }
    }

    // The workspace revision is the only mutable input to finalization; the fixture keeps
    // it exact so a finalized snapshot cannot be created against a later revision.
    private sealed class ApplicationRevision : IWorkspaceRevisionStore
    {
        public Guid Id { get; } = Guid.NewGuid();
        public long Revision { get; private set; } = 3;
        public Task<long?> GetRevisionAsync(Guid applicationId, string sequenceNumber, CancellationToken cancellationToken = default) =>
            Task.FromResult<long?>(applicationId == Id && sequenceNumber == "0001" ? Revision : null);
        public Task<IAsyncDisposable> LockApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IAsyncDisposable>(new Noop());
        public Task<bool> AdvanceAsync(Guid applicationId, string sequenceNumber, long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            if (applicationId != Id || sequenceNumber != "0001" || Revision != expectedRevision) return Task.FromResult(false);
            Revision++;
            return Task.FromResult(true);
        }
        public void AdvanceRevision() => Revision++;
        private sealed class Noop : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
