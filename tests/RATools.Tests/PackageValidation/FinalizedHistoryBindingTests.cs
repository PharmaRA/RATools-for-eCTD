using System.Text.Json;
using Microsoft.Extensions.Options;
using RATools.Application.Abstractions.Security;
using RATools.Application.Finalization;
using RATools.Application.PackageValidation;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;
using RATools.Infrastructure.Security;
using RATools.Infrastructure.Storage;

namespace RATools.Tests.PackageValidation;

// P3-07: verified history is derived from a finalized snapshot that recorded exactly the
// selected bytes. Nothing a caller sends can assert trust, and a verified claim stops being
// valid when its snapshot no longer exists or no longer binds that content.
public sealed class FinalizedHistoryBindingTests
{
    private static readonly DateTime Now = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ASelectedHistoryWithoutAFinalizedSnapshotStaysUnverified()
    {
        await using var fixture = new Fixture();
        var zero = await fixture.RegisterAsync("0000");
        var one = await fixture.RegisterAsync("0001");
        var run = await fixture.EnqueueAsync(one, zero);
        var entry = Assert.Single(Fixture.BaselineEntries(await fixture.EvidenceAsync(run)));
        Assert.Equal(HistorySourceKind.ImportedExternal.ToString(), entry.GetProperty("sourceKind").GetString());
        Assert.Equal(HistoryTrustStatus.Unverified.ToString(), entry.GetProperty("trustStatus").GetString());
        Assert.Equal(JsonValueKind.Null, entry.GetProperty("finalizedSequenceId").ValueKind);
    }

    [Fact]
    public async Task AFinalizedSnapshotOfTheSelectedBytesMakesHistoryVerified()
    {
        await using var fixture = new Fixture();
        var zero = await fixture.RegisterAsync("0000");
        var one = await fixture.RegisterAsync("0001");
        var snapshot = await fixture.RegisterFinalizedAsync("0000", zero.InputDigest);

        var run = await fixture.EnqueueAsync(one, zero);
        var entry = Assert.Single(Fixture.BaselineEntries(await fixture.EvidenceAsync(run)));
        Assert.Equal(HistorySourceKind.Finalized.ToString(), entry.GetProperty("sourceKind").GetString());
        Assert.Equal(HistoryTrustStatus.VerifiedForScope.ToString(), entry.GetProperty("trustStatus").GetString());
        Assert.Equal(snapshot.Id, entry.GetProperty("finalizedSequenceId").GetGuid());
        Assert.Equal(snapshot.ValidationRunId, entry.GetProperty("validationRunId").GetGuid());
        // The run is executable and its archived baseline still proves the claim.
        var lease = (await fixture.Store.ClaimAsync("worker", Now, TimeSpan.FromMinutes(1)))!;
        var report = await fixture.Service.InspectAsync(lease.Run);
        Assert.Equal(ValidationRunStatus.Completed, report.RunStatus);
        Assert.Contains(report.Coverage, rule => rule.RuleId == "HISTORY-BASELINE" && rule.CheckStatus != CheckStatus.Fail);
    }

    [Fact]
    public async Task ARemovedSnapshotInvalidatesTheArchivedVerifiedHistory()
    {
        await using var fixture = new Fixture();
        var zero = await fixture.RegisterAsync("0000");
        var one = await fixture.RegisterAsync("0001");
        var snapshot = await fixture.RegisterFinalizedAsync("0000", zero.InputDigest);
        var run = await fixture.EnqueueAsync(one, zero);
        // The archive still claims verification; the snapshot that proved it is gone.
        fixture.RemoveSnapshot(snapshot.Id);
        var lease = (await fixture.Store.ClaimAsync("worker", Now, TimeSpan.FromMinutes(1)))!;
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Service.InspectAsync(lease.Run));
    }

    [Fact]
    public async Task AFinalizedSnapshotOfOtherBytesCannotVerifyTheSelection()
    {
        await using var fixture = new Fixture();
        var zero = await fixture.RegisterAsync("0000");
        var one = await fixture.RegisterAsync("0001");
        // The snapshot recorded different content than the selected input.
        await fixture.RegisterFinalizedAsync("0000", new string('9', 64));
        var run = await fixture.EnqueueAsync(one, zero);
        var entry = Assert.Single(Fixture.BaselineEntries(await fixture.EvidenceAsync(run)));
        Assert.Equal(HistoryTrustStatus.Unverified.ToString(), entry.GetProperty("trustStatus").GetString());
        Assert.Equal(HistorySourceKind.ImportedExternal.ToString(), entry.GetProperty("sourceKind").GetString());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly PackageFileInspectorTests.Fixture _files = new();
        private readonly PackageInputReader _reader;
        private readonly Guid _application = Guid.NewGuid();
        public InMemoryPackageValidationStore Store { get; } = new();
        public HideableFinalizationStore Finalizations { get; } = new();
        public PackageValidationService Service { get; }
        private readonly PackageValidationInputRegistry _registry;

        public Fixture()
        {
            var options = Options.Create(new PackageValidationOptions
            {
                StorageRoot = _files.ArchivePath("validation")
            });
            var policy = new ConfiguredWorkspacePathPolicy(Options.Create(
                new SecurityOptions { AllowedWorkspaceRoots = [_files.PathOf("")] }));
            _reader = new PackageInputReader(Path.Combine(_files.ArchivePath("validation"), "captures"));
            _registry = new(Store, _reader, policy, options);
            Service = new PackageValidationService(Store, _reader, policy,
                new PackageDeliveryInspector(new PdfPigPackageInspector()),
                finalizedHistory: new FinalizedHistoryResolver(Finalizations));
        }

        public Task<PackageInputDto> RegisterAsync(string sequence) =>
            _registry.RegisterAsync(new(_application, _files.PathOf(sequence)));

        public async Task<FinalizedSequence> RegisterFinalizedAsync(string sequence, string contentDigest)
        {
            // A finalized snapshot records the validated input digest of the history it
            // delivered; the resolver matches on exactly that value.
            var snapshot = FinalizedSequence.Create(_application, sequence, 1, Digest("manifest-" + sequence),
                new string('a', 64), "3.2.2", "3.3", Digest("baseline-" + sequence), Guid.NewGuid(), contentDigest,
                Digest("report-" + sequence), PackageValidationMode.Development, false, [],
                FinalizationService.ToolVersion, "history-" + sequence + "-" + Guid.NewGuid().ToString("N"), Now);
            await Finalizations.RegisterAsync(snapshot);
            return snapshot;
        }

        public void RemoveSnapshot(Guid id) => Finalizations.Hide(id);

        public Task<PackageRunEnqueueResult> EnqueueAsync(PackageInputDto target, PackageInputDto history) =>
            Service.EnqueueAsync(new(target.Id, PackageValidationCatalog.UsProfile, PackageValidationMode.Development,
                "history-" + Guid.NewGuid().ToString("N"), History: [new(history.Id, PackageValidationCatalog.UsProfile)]));

        public async Task<JsonElement> EvidenceAsync(PackageRunEnqueueResult run)
        {
            var stored = await Store.GetRunAsync(run.Run.Id);
            Assert.NotNull(stored);
            using var document = JsonDocument.Parse(stored!.RequestJson);
            return document.RootElement.GetProperty("historyBaseline").Clone();
        }

        public static JsonElement[] BaselineEntries(JsonElement baseline) =>
            baseline.GetProperty("entries").EnumerateArray().Select(entry => entry.Clone()).ToArray();

        public ValueTask DisposeAsync() => _files.DisposeAsync();
    }

    // The in-memory store is immutable by contract; "removal" is simulated by making the
    // snapshot unreadable, which is what a lost or replaced record looks like to a reader.
    private sealed class HideableFinalizationStore : IFinalizationStore
    {
        private readonly InMemoryFinalizationStore _inner = new();
        private readonly HashSet<Guid> _hidden = [];

        public Task<FinalizationRegisterResult> RegisterAsync(FinalizedSequence sequence, CancellationToken cancellationToken = default) =>
            _inner.RegisterAsync(sequence, cancellationToken);

        public Task<FinalizedSequence?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
            _hidden.Contains(id) ? Task.FromResult<FinalizedSequence?>(null) : _inner.GetAsync(id, cancellationToken);

        public Task<FinalizedSequence?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken = default) =>
            _inner.FindByIdempotencyKeyAsync(idempotencyKey, cancellationToken);

        public async Task<IReadOnlyList<FinalizedSequence>> ListBySequenceAsync(Guid applicationId, string sequenceNumber,
            CancellationToken cancellationToken = default) =>
            (await _inner.ListBySequenceAsync(applicationId, sequenceNumber, cancellationToken))
            .Where(snapshot => !_hidden.Contains(snapshot.Id)).ToArray();

        public async Task<FinalizedSequence?> LatestBySequenceAsync(Guid applicationId, string sequenceNumber,
            CancellationToken cancellationToken = default)
        {
            var all = await ListBySequenceAsync(applicationId, sequenceNumber, cancellationToken);
            return all.Count == 0 ? null : all[^1];
        }

        public Task<bool> AnyByApplicationAsync(Guid applicationId, string? sequenceNumber = null,
            CancellationToken cancellationToken = default) =>
            _inner.AnyByApplicationAsync(applicationId, sequenceNumber, cancellationToken);

        public Task<FinalizedDraftRevision?> FindDraftAsync(Guid finalizedSequenceId,
            CancellationToken cancellationToken = default) =>
            _inner.FindDraftAsync(finalizedSequenceId, cancellationToken);

        public Task<FinalizedDraftRevision?> FindDraftByIdempotencyKeyAsync(string idempotencyKey,
            CancellationToken cancellationToken = default) =>
            _inner.FindDraftByIdempotencyKeyAsync(idempotencyKey, cancellationToken);

        public Task<FinalizedDraftRevision> AppendDraftAsync(FinalizedDraftRevision draft,
            CancellationToken cancellationToken = default) => _inner.AppendDraftAsync(draft, cancellationToken);

        public Task<IReadOnlyList<DeliveryRecord>> ListDeliveriesAsync(Guid finalizedSequenceId,
            CancellationToken cancellationToken = default) => _inner.ListDeliveriesAsync(finalizedSequenceId, cancellationToken);

        public Task AppendDeliveryAsync(DeliveryRecord record, CancellationToken cancellationToken = default) =>
            _inner.AppendDeliveryAsync(record, cancellationToken);

        public Task<bool> DeliveryExistsAsync(Guid deliveryRecordId, CancellationToken cancellationToken = default) =>
            _inner.DeliveryExistsAsync(deliveryRecordId, cancellationToken);

        public Task<IReadOnlyList<DeliveryReceipt>> ListReceiptsAsync(Guid deliveryRecordId,
            CancellationToken cancellationToken = default) => _inner.ListReceiptsAsync(deliveryRecordId, cancellationToken);

        public Task AppendReceiptAsync(DeliveryReceipt receipt, CancellationToken cancellationToken = default) =>
            _inner.AppendReceiptAsync(receipt, cancellationToken);

        public void Hide(Guid id) => _hidden.Add(id);
    }

    private static string Digest(string seed) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)))
            .ToLowerInvariant();
}
