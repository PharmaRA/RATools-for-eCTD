using RATools.Application.Abstractions.Persistence;
using RATools.Application.Finalization;
using RATools.Application.PackageValidation;
using RATools.Application.Persistence;
using RATools.Application.Workspaces;
using RATools.Domain.Finalization;
using RATools.Domain.PackageValidation;
using RATools.Infrastructure.Persistence.InMemory;

namespace RATools.Tests.Finalization;

// P4-07: once a sequence is finalized, every draft write is refused until an explicit
// continuation records which delivered version the edits belong to.
public sealed class FinalizedWorkspaceBoundaryTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task UnfinalizedSequenceIsEditable()
    {
        var fixture = new Fixture();
        Assert.Null(await fixture.Guard.GetStateAsync(fixture.ApplicationId, "0001"));
        await fixture.Guard.EnsureEditableAsync(fixture.ApplicationId, "0001");
        var revision = fixture.Revision;
        await using var mutation = await fixture.Coordinator.AcquireAsync(fixture.ApplicationId, "0001", revision);
        await mutation.CommitAsync(_ => Task.CompletedTask);
        Assert.Equal(revision + 1, mutation.Revision);
    }

    [Fact]
    public async Task FinalizedSequenceRejectsEveryWriteUntilADraftIsOpened()
    {
        var fixture = new Fixture();
        var finalizedAt = fixture.Revision;
        var snapshot = await fixture.RegisterSnapshotAsync(finalizedAt);
        var state = await fixture.Guard.GetStateAsync(fixture.ApplicationId, "0001");
        Assert.NotNull(state);
        Assert.Equal(snapshot.Id, state!.FinalizedSequenceId);
        Assert.False(state.IsEditable);

        var blocked = await Assert.ThrowsAsync<WorkspaceFinalizedException>(() =>
            fixture.Coordinator.AcquireAsync(fixture.ApplicationId, "0001", finalizedAt));
        Assert.Equal(snapshot.Id, blocked.FinalizedSequenceId);
        Assert.Equal(finalizedAt, blocked.SourceWorkspaceRevision);

        var opened = await fixture.Drafts.OpenAsync(new(snapshot.Id, finalizedAt, "draft-1"));
        Assert.True(opened.Created);
        Assert.Equal(snapshot.Id, opened.Draft.FinalizedSequenceId);
        Assert.Equal(finalizedAt, opened.Draft.BaseWorkspaceRevision);
        Assert.Equal(finalizedAt + 1, opened.Draft.WorkspaceRevision);
        Assert.True((await fixture.Guard.GetStateAsync(fixture.ApplicationId, "0001"))!.IsEditable);

        // The workspace now sits at the revision the continuation advanced to.
        await using var mutation = await fixture.Coordinator.AcquireAsync(fixture.ApplicationId, "0001",
            opened.Draft.WorkspaceRevision);
        await mutation.CommitAsync(_ => Task.CompletedTask);
        // A replay with the same key returns the committed draft, never a second row.
        var replay = await fixture.Drafts.OpenAsync(new(snapshot.Id, finalizedAt, "draft-1"));
        Assert.False(replay.Created);
        Assert.Equal(opened.Draft.Id, replay.Draft.Id);
    }

    [Fact]
    public async Task OnlyTheLatestSnapshotMayBeContinuedAndOnlyOnce()
    {
        var fixture = new Fixture();
        var older = await fixture.RegisterSnapshotAsync(fixture.Revision, key: "older");
        await fixture.AdvanceAsync();
        var revised = fixture.Revision;
        // A second, later snapshot supersedes the first.
        var newer = await fixture.RegisterSnapshotAsync(revised, key: "newer");
        var superseded = await Assert.ThrowsAsync<FinalizationDraftRequiresLatestException>(() =>
            fixture.Drafts.OpenAsync(new(older.Id, revised, "from-older")));
        Assert.Equal(newer.Id, superseded.LatestFinalizedSequenceId);

        var opened = await fixture.Drafts.OpenAsync(new(newer.Id, revised, "from-latest"));
        Assert.True(opened.Created);
        await Assert.ThrowsAsync<FinalizationDraftAlreadyOpenException>(() =>
            fixture.Drafts.OpenAsync(new(newer.Id, revised + 1, "second-draft")));
    }

    [Fact]
    public async Task StaleContinuationRevisionIsAConflictNotAPartialDraft()
    {
        var fixture = new Fixture();
        var revision = fixture.Revision;
        var snapshot = await fixture.RegisterSnapshotAsync(revision);
        await Assert.ThrowsAsync<WorkspaceRevisionConflictException>(() =>
            fixture.Drafts.OpenAsync(new(snapshot.Id, revision + 5, "stale")));
        Assert.Null(await fixture.Drafts.FindAsync(snapshot.Id));
        Assert.False((await fixture.Guard.GetStateAsync(fixture.ApplicationId, "0001"))!.IsEditable);
    }

    [Fact]
    public async Task AFinalizationRacingTheLockSupersedesTheContinuation()
    {
        var fixture = new Fixture();
        var revision = fixture.Revision;
        var older = await fixture.RegisterSnapshotAsync(revision, key: "older");
        // A finalization commits after the draft request read "latest" but before it holds
        // the application lock. The under-lock re-check must refuse the now-superseded
        // snapshot instead of advancing the revision for a continuation nothing continues.
        FinalizedSequence? newer = null;
        fixture.Store.OnLock = async () => newer = await fixture.RegisterSnapshotAsync(revision, key: "newer");

        var superseded = await Assert.ThrowsAsync<FinalizationDraftRequiresLatestException>(() =>
            fixture.Drafts.OpenAsync(new(older.Id, revision, "continue-older")));
        Assert.Equal(newer!.Id, superseded.LatestFinalizedSequenceId);
        Assert.Null(await fixture.Drafts.FindAsync(older.Id));
        Assert.Equal(revision, fixture.Revision);
    }

    [Fact]
    public async Task ContinuationRequiresAnExactIdempotencyKeyForItsSnapshot()
    {
        var fixture = new Fixture();
        var first = await fixture.RegisterSnapshotAsync(fixture.Revision, key: "first");
        await fixture.Drafts.OpenAsync(new(first.Id, fixture.Revision, "shared-key"));
        await fixture.AdvanceAsync();
        var revised = fixture.Revision;
        var second = await fixture.RegisterSnapshotAsync(revised, key: "second");
        // The same key cannot be reused to continue a different delivered version.
        await Assert.ThrowsAsync<FinalizationIdempotencyConflictException>(() =>
            fixture.Drafts.OpenAsync(new(second.Id, revised, "shared-key")));
    }

    private sealed class Fixture
    {
        public Guid ApplicationId { get; } = Guid.NewGuid();
        public long Revision => Store.Revision;
        public InMemoryFinalizationStore Finalizations { get; } = new();
        public Revisions Store { get; } = new();
        public FinalizedWorkspaceGuard Guard { get; }
        public WorkspaceMutationCoordinator Coordinator { get; }
        public FinalizationDraftService Drafts { get; }

        public Fixture()
        {
            Store.Add(ApplicationId, "0001", 3);
            Guard = new(Finalizations);
            Coordinator = new(Store, new PassthroughPersistenceTransaction(), null, Guard);
            Drafts = new(Finalizations, Coordinator);
        }

        public async Task<FinalizedSequence> RegisterSnapshotAsync(long sourceRevision, string? key = null)
        {
            // One snapshot per exact content: each test snapshot here stands for different
            // delivered bytes, so the manifest digest must differ too. Registration time
            // advances so "latest" is unambiguous.
            var manifest = Digest("manifest-" + (key ?? Guid.NewGuid().ToString("N")));
            var snapshot = FinalizedSequence.Create(ApplicationId, "0001", sourceRevision, manifest,
                PackageValidationCatalog.UsProfile, "3.2.2", null, Digest("history"), Guid.NewGuid(),
                Digest("input"), Digest("report"), PackageValidationMode.Development, false, [],
                FinalizationService.ToolVersion, key ?? Guid.NewGuid().ToString("N"), Now.AddMinutes(_registered++));
            var registered = await Finalizations.RegisterAsync(snapshot);
            return registered.Sequence;
        }

        private int _registered;

        public async Task AdvanceAsync() =>
            Assert.True(await Store.AdvanceAsync(ApplicationId, "0001", Store.Revision));
    }

    private static string Digest(string seed) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(seed)))
            .ToLowerInvariant();

    // A minimal revision store: one sequence whose revision the test can advance.
    private sealed class Revisions : IWorkspaceRevisionStore
    {
        private readonly Dictionary<(Guid, string), long> _revisions = [];
        private (Guid ApplicationId, string SequenceNumber) _current;
        public long Revision => _revisions[_current];
        public void Add(Guid applicationId, string sequence, long revision)
        {
            _current = (applicationId, sequence);
            _revisions[_current] = revision;
        }

        // Lets a test commit work "between" the service's unlocked read and its lock.
        public Func<Task>? OnLock { get; set; }
        public async Task<IAsyncDisposable> LockApplicationAsync(Guid applicationId, CancellationToken cancellationToken = default)
        {
            if (OnLock is not null) await OnLock();
            return new Noop();
        }
        public Task<long?> GetRevisionAsync(Guid applicationId, string sequenceNumber,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_revisions.TryGetValue((applicationId, sequenceNumber), out var revision)
                ? (long?)revision : null);
        public Task<bool> AdvanceAsync(Guid applicationId, string sequenceNumber, long expectedRevision,
            CancellationToken cancellationToken = default)
        {
            if (!_revisions.TryGetValue((applicationId, sequenceNumber), out var revision) || revision != expectedRevision)
                return Task.FromResult(false);
            _revisions[(applicationId, sequenceNumber)] = revision + 1;
            return Task.FromResult(true);
        }
        private sealed class Noop : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }
}
