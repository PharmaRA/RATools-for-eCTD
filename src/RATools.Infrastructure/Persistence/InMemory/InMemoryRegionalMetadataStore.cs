using System.Text.Json;
using RATools.Application.RegionalMetadata;
using RATools.Domain.RegionalMetadata;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryRegionalMetadataStore : IRegionalMetadataStore
{
    private readonly object _gate = new();
    private readonly Dictionary<Guid, string> _revisions = [];
    private readonly Dictionary<(Guid App, string Scope), Guid> _heads = [];
    private sealed class Pending
    {
        public Dictionary<Guid, string> Revisions { get; } = [];
        public Dictionary<(Guid App, string Scope), (Guid? Before, Guid After)> Heads { get; } = [];
    }
    private Pending? Changes(bool create)
    {
        var transaction = InMemoryPersistenceTransaction.Current.Value;
        if (transaction is null) return null;
        if (transaction.Changes.TryGetValue(this, out var existing)) return (Pending)existing;
        if (!create) return null;
        var pending = new Pending(); transaction.Changes.Add(this, pending);
        transaction.Commits.Insert(0, () =>
        {
            lock (_gate)
            {
                foreach (var (scope, head) in pending.Heads)
                    if (CurrentHead(scope) != head.Before) throw new RegionalMetadataConflictException();
                foreach (var (id, data) in pending.Revisions) _revisions.Add(id, data);
                foreach (var (scope, head) in pending.Heads) _heads[scope] = head.After;
            }
        });
        return pending;
    }
    public Task<RegionalMetadataRevision?> GetHeadAsync(Guid applicationId, string? sequenceNumber, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); lock (_gate)
        {
            var scope = (applicationId, sequenceNumber ?? "application"); var changes = Changes(false);
            var id = changes?.Heads.TryGetValue(scope, out var pending) == true ? pending.After : CurrentHead(scope);
            return Task.FromResult(id is { } value ? Read(value, changes) : null);
        }
    }
    public Task<RegionalMetadataRevision?> GetRevisionAsync(Guid id, CancellationToken cancellationToken = default)
    { cancellationToken.ThrowIfCancellationRequested(); lock (_gate) return Task.FromResult(Read(id, Changes(false))); }
    public Task<RegionalMetadataRevision?> FindLegacyAsync(Guid applicationId, string sequenceNumber, string legacyDigest, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); lock (_gate)
        {
            var all = _revisions.Concat(Changes(false)?.Revisions ?? []).Select(pair => Parse(pair.Value));
            return Task.FromResult(all.SingleOrDefault(item => item.ApplicationId == applicationId && item.SequenceNumber == sequenceNumber && item.LegacyDigest == legacyDigest));
        }
    }
    public Task<RegionalMetadataRevision> AppendAsync(RegionalMetadataRevision revision, Guid? expectedHeadId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); revision.Verify(); lock (_gate)
        {
            var existing = Read(revision.Id, Changes(false));
            if (existing is not null)
            { if (JsonSerializer.Serialize(existing) != JsonSerializer.Serialize(revision)) throw new InvalidDataException("An immutable regional revision cannot be replaced."); return Task.FromResult(existing); }
            var changes = Changes(true) ?? throw new InvalidOperationException("Regional metadata appends require an ambient persistence transaction.");
            var scope = (revision.ApplicationId, revision.ScopeKey);
            var current = changes.Heads.TryGetValue(scope, out var staged) ? staged.After : CurrentHead(scope);
            if (current != expectedHeadId) throw new RegionalMetadataConflictException();
            var before = current is { } id ? Read(id, changes) : null;
            if (revision.MetadataRevision != (before?.MetadataRevision ?? 0) + 1) throw new RegionalMetadataConflictException();
            changes.Revisions.Add(revision.Id, JsonSerializer.Serialize(revision)); changes.Heads[scope] = (changes.Heads.TryGetValue(scope, out staged) ? staged.Before : current, revision.Id);
            return Task.FromResult(Parse(changes.Revisions[revision.Id]));
        }
    }
    private Guid? CurrentHead((Guid App, string Scope) scope) => _heads.TryGetValue(scope, out var id) ? id : null;
    private RegionalMetadataRevision? Read(Guid id, Pending? pending) => pending?.Revisions.TryGetValue(id, out var data) == true ? Parse(data) : _revisions.TryGetValue(id, out data) ? Parse(data) : null;
    private static RegionalMetadataRevision Parse(string data) { var revision = JsonSerializer.Deserialize<RegionalMetadataRevision>(data) ?? throw new InvalidDataException(); revision.Verify(); return revision; }
}
