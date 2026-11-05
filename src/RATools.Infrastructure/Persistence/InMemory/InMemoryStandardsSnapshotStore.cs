using System.Text.Json;
using RATools.Application.Standards;
using RATools.Domain.Standards;

namespace RATools.Infrastructure.Persistence.InMemory;

public sealed class InMemoryStandardsSnapshotStore : IStandardsSnapshotStore
{
    private readonly Dictionary<string, string> _snapshots = new(StringComparer.Ordinal); private readonly object _gate = new();
    public Task<StandardsProfileSnapshot> AddAsync(StandardsProfileSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        snapshot.Verify(); lock (_gate)
        {
            if (_snapshots.TryGetValue(snapshot.Id, out var data)) return Task.FromResult(Read(data));
            _snapshots.Add(snapshot.Id, JsonSerializer.Serialize(snapshot)); return Task.FromResult(Read(_snapshots[snapshot.Id]));
        }
    }
    public Task<StandardsProfileSnapshot?> GetAsync(string id, CancellationToken cancellationToken = default)
    { lock (_gate) return Task.FromResult(_snapshots.TryGetValue(id, out var data) ? Read(data) : null); }
    private static StandardsProfileSnapshot Read(string data) { var snapshot = JsonSerializer.Deserialize<StandardsProfileSnapshot>(data) ?? throw new InvalidDataException(); snapshot.Verify(); return snapshot; }
}
