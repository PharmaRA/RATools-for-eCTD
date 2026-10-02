using System.Collections.Concurrent;

namespace RATools.Infrastructure.Persistence.InMemory;

internal sealed class TransactionalMemoryCollection<T>(Func<T, T> clone) where T : class
{
    private readonly ConcurrentDictionary<Guid, T> _items = new();

    private Dictionary<Guid, T?>? Changes
    {
        get
        {
            var transaction = InMemoryPersistenceTransaction.Current.Value;
            if (transaction is null) return null;
            if (transaction.Changes.TryGetValue(this, out var existing)) return (Dictionary<Guid, T?>)existing;
            var changes = new Dictionary<Guid, T?>();
            transaction.Changes.Add(this, changes);
            transaction.Commits.Add(() =>
            {
                foreach (var (id, value) in changes)
                {
                    if (value is null) _items.TryRemove(id, out _);
                    else _items[id] = value;
                }
            });
            return changes;
        }
    }

    public T? Get(Guid id)
    {
        T? value;
        if (Changes is { } changes && changes.TryGetValue(id, out value)) return value is null ? null : clone(value);
        return _items.TryGetValue(id, out value) ? clone(value) : null;
    }

    public void Set(Guid id, T value)
    {
        var copy = clone(value);
        if (Changes is { } changes) changes[id] = copy;
        else _items[id] = copy;
    }

    public bool Update(Guid id, T value)
    {
        if (Get(id) is null) return false;
        Set(id, value);
        return true;
    }

    public void Remove(Guid id)
    {
        if (Changes is { } changes) changes[id] = null;
        else _items.TryRemove(id, out _);
    }

    public IReadOnlyCollection<T> Values
    {
        get
        {
            var values = _items.ToDictionary(item => item.Key, item => item.Value);
            if (Changes is { } changes)
                foreach (var (id, value) in changes)
                {
                    if (value is null) values.Remove(id);
                    else values[id] = value;
                }
            return values.Values.Select(clone).ToArray();
        }
    }
}
