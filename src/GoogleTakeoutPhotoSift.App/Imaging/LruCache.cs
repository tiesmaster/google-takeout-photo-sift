namespace Tiesmaster.GoogleTakeoutPhotoSift.App.Imaging;

/// <summary>A thread-safe least-recently-used cache with a budget in bytes.</summary>
public sealed class LruCache<TKey, TValue>(long budgetBytes, Func<TValue, long> sizeOf, IEqualityComparer<TKey>? comparer = null)
    where TKey : notnull
{
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value, long Size)>> _map = new(comparer);
    private readonly LinkedList<(TKey Key, TValue Value, long Size)> _order = new();
    private readonly Lock _lock = new();
    private long _size;

    public long SizeBytes
    {
        get
        {
            lock (_lock)
                return _size;
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _order.Remove(node);
                _order.AddFirst(node);
                value = node.Value.Value;
                return true;
            }
        }
        value = default!;
        return false;
    }

    public void Set(TKey key, TValue value)
    {
        var size = sizeOf(value);
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var old))
            {
                _order.Remove(old);
                _size -= old.Value.Size;
            }
            _map[key] = _order.AddFirst((key, value, size));
            _size += size;
            // Keep at least the newest entry, even when it alone is over budget.
            while (_size > budgetBytes && _order.Count > 1)
            {
                var last = _order.Last!;
                _order.RemoveLast();
                _map.Remove(last.Value.Key);
                _size -= last.Value.Size;
            }
        }
    }
}
