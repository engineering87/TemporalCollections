// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe temporal multimap that associates a key with multiple timestamped values.
    /// Each insertion produces a <see cref="TemporalItem{T}"/> stamped with a strictly increasing UTC timestamp.
    ///
    /// This collection does NOT implement IEnumerable by design; consumers should use the ITimeQueryable API.
    /// Global time-based queries operate on (Key, Value) pairs using <see cref="KeyValuePair{TKey, TValue}"/> as the value type T.
    /// </summary>
    public class TemporalMultimap<TKey, TValue> : TimeQueryableBase<KeyValuePair<TKey, TValue>>
        where TKey : notnull
    {
        private readonly Dictionary<TKey, List<TemporalItem<KeyValuePair<TKey, TValue>>>> _byKey = [];
        private int _count;
        private readonly Lock _lock = new();

        /// <summary>Returns the total number of items currently stored (across all keys).</summary>
        public int Count 
        { 
            get 
            {
                lock (_lock)
                { 
                    return _count;
                } 
            } 
        }

        /// <summary>Returns the current number of distinct keys stored.</summary>
        public int KeyCount 
        { 
            get 
            {
                lock (_lock)
                { 
                    return _byKey.Count;
                } 
            }
        }

        // ---------- Mutation API ----------

        /// <summary>
        /// Adds a new (key, value) pair, stamping it with a monotonic UTC timestamp.
        /// Returns the created <see cref="TemporalItem{T}"/>.
        /// </summary>
        public TemporalItem<KeyValuePair<TKey, TValue>> AddValue(TKey key, TValue value)
        {
            var item = TemporalItem<KeyValuePair<TKey, TValue>>.Create(new KeyValuePair<TKey, TValue>(key, value));
            lock (_lock)
            {
                if (!_byKey.TryGetValue(key, out var list))
                {
                    list = new List<TemporalItem<KeyValuePair<TKey, TValue>>>(4);
                    _byKey[key] = list;
                }
                list.Add(item); // monotonic ticks → append fast path
                _count++;
            }
            return item;
        }

        /// <summary>
        /// Adds a pre-built temporal item (must carry (key, value)).
        /// Preserves per-key non-decreasing timestamp order (binary inserts if needed).
        /// </summary>
        public void Add(TemporalItem<KeyValuePair<TKey, TValue>> item)
        {
            var key = item.Value.Key;
            lock (_lock)
            {
                if (!_byKey.TryGetValue(key, out var list))
                {
                    list = new List<TemporalItem<KeyValuePair<TKey, TValue>>>(4);
                    _byKey[key] = list;
                }

                if (list.Count == 0 || list[^1].Timestamp.UtcTicks <= item.Timestamp.UtcTicks)
                    list.Add(item);
                else
                    list.Insert(LowerBound(list, item.Timestamp.UtcTicks), item);

                _count++;
            }
        }

        /// <summary>
        /// Adds a sequence of values for the same key using <see cref="AddValue(TKey, TValue)"/>.
        /// </summary>
        public void AddRange(TKey key, IEnumerable<TValue> values)
        {
            foreach (var v in values) 
                AddValue(key, v);
        }

        /// <summary>
        /// Adds a sequence of pre-built temporal items (KeyValuePair of (key, value)).
        /// </summary>
        public void AddRange(IEnumerable<TemporalItem<KeyValuePair<TKey, TValue>>> items)
        {
            foreach (var it in items) 
                Add(it);
        }

        /// <summary>
        /// Removes all entries for the specified key.
        /// </summary>
        public bool RemoveKey(TKey key)
        {
            lock (_lock)
            {
                if (_byKey.Remove(key, out var list))
                {
                    _count -= list.Count;
                    return true;
                }
                return false;
            }
        }

        /// <summary>
        /// Removes all entries for the specified key strictly older than <paramref name="cutoff"/>.
        /// </summary>
        public int RemoveOlderThan(TKey key, DateTimeOffset cutoff)
        {
            lock (_lock)
            {
                if (!_byKey.TryGetValue(key, out var list) || list.Count == 0) 
                    return 0;

                long c = cutoff.UtcTicks;
                int idx = LowerBound(list, c); // first >= cutoff → [0..idx-1] are strictly older
                if (idx <= 0) 
                    return 0;

                list.RemoveRange(0, idx);
                _count -= idx;

                if (list.Count == 0) 
                    _byKey.Remove(key);
                return idx;
            }
        }

        /// <summary>
        /// Removes all entries for the specified key whose timestamps fall within [from, to] inclusive.
        /// </summary>
        public int RemoveRange(TKey key, DateTimeOffset from, DateTimeOffset to)
        {
            lock (_lock)
            {
                if (!_byKey.TryGetValue(key, out var list) || list.Count == 0) 
                    return 0;

                long f = from.UtcTicks, t = to.UtcTicks;
                if (f > t) 
                    (f, t) = (t, f);
                int i0 = LowerBound(list, f);
                int i1 = UpperExclusive(list, t);
                int remove = Math.Max(0, i1 - i0);
                if (remove == 0) 
                    return 0;

                list.RemoveRange(i0, remove);
                _count -= remove;

                if (list.Count == 0) 
                    _byKey.Remove(key);
                return remove;
            }
        }

        /// <inheritdoc/>
        public override void Clear()
        {
            lock (_lock)
            {
                _byKey.Clear();
                _count = 0;
            }
        }

        // ---------- Per-key read helpers ----------

        /// <summary>
        /// Returns items for a specific key within [from, to] inclusive, ordered by timestamp.
        /// </summary>
        public IEnumerable<TemporalItem<TValue>> GetValuesInRange(TKey key, DateTimeOffset from, DateTimeOffset to)
        {
            lock (_lock)
            {
                if (!_byKey.TryGetValue(key, out var list) || list.Count == 0)
                    return [];

                long f = from.UtcTicks, t = to.UtcTicks;
                if (f > t) 
                    (f, t) = (t, f);
                int i0 = LowerBound(list, f);
                int i1 = UpperExclusive(list, t);
                if (i0 >= i1) 
                    return [];

                var res = new TemporalItem<TValue>[i1 - i0];
                int pos = 0;
                for (int i = i0; i < i1; i++)
                {
                    var kv = list[i].Value;
                    res[pos++] = new TemporalItem<TValue>(kv.Value, list[i].Timestamp);
                }
                return res;
            }
        }

        /// <summary>Returns the number of items stored under a specific key.</summary>
        public int CountForKey(TKey key)
        {
            lock (_lock)
                return _byKey.TryGetValue(key, out var list) ? list.Count : 0;
        }

        /// <summary>Checks whether the multimap contains the specified key.</summary>
        public bool ContainsKey(TKey key)
        {
            lock (_lock)
                return _byKey.ContainsKey(key);
        }

        // ---------- TimeQueryableBase<T> (global) ----------

        /// <inheritdoc/>
        public override IEnumerable<TemporalItem<KeyValuePair<TKey, TValue>>> GetInRange(DateTimeOffset from, DateTimeOffset to)
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return [];
                long f = from.UtcTicks, t = to.UtcTicks; 
                if (f > t) 
                    (f, t) = (t, f);

                var acc = new List<TemporalItem<KeyValuePair<TKey, TValue>>>();
                foreach (var list in _byKey.Values)
                {
                    if (list.Count == 0) continue;
                    if (list[^1].Timestamp.UtcTicks < f) continue;
                    if (list[0].Timestamp.UtcTicks > t) continue;

                    int i0 = LowerBound(list, f);
                    int i1 = UpperExclusive(list, t);
                    for (int i = i0; i < i1; i++) 
                        acc.Add(list[i]);
                }

                if (acc.Count <= 1) 
                    return acc.ToArray();
                acc.Sort((a, b) => a.Timestamp.UtcTicks.CompareTo(b.Timestamp.UtcTicks));
                return acc.ToArray();
            }
        }

        /// <inheritdoc/>
        public override void RemoveOlderThan(DateTimeOffset cutoff)
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return;
                long c = cutoff.UtcTicks;

                var emptyKeys = new List<TKey>();
                foreach (var (key, list) in _byKey)
                {
                    if (list.Count == 0) 
                    { 
                        emptyKeys.Add(key); 
                        continue; 
                    }

                    if (list[^1].Timestamp.UtcTicks < c)
                    {
                        _count -= list.Count;
                        emptyKeys.Add(key);
                        continue;
                    }

                    if (list[0].Timestamp.UtcTicks < c)
                    {
                        int idx = LowerBound(list, c);
                        if (idx > 0)
                        {
                            _count -= idx;
                            list.RemoveRange(0, idx);
                        }
                    }
                }

                foreach (var k in emptyKeys) 
                    _byKey.Remove(k);
            }
        }

        /// <inheritdoc/>
        public override int CountInRange(DateTimeOffset from, DateTimeOffset to)
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return 0;
                long f = from.UtcTicks, t = to.UtcTicks; 
                if (f > t) 
                    (f, t) = (t, f);

                int total = 0;
                foreach (var list in _byKey.Values)
                {
                    if (list.Count == 0) continue;
                    if (list[^1].Timestamp.UtcTicks < f) continue;
                    if (list[0].Timestamp.UtcTicks > t) continue;

                    int i0 = LowerBound(list, f);
                    int i1 = UpperExclusive(list, t);
                    total += Math.Max(0, i1 - i0);
                }
                return total;
            }
        }

        /// <inheritdoc/>
        public override void RemoveRange(DateTimeOffset from, DateTimeOffset to)
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return;
                long f = from.UtcTicks, t = to.UtcTicks; 
                if (f > t) 
                    (f, t) = (t, f);

                var emptyKeys = new List<TKey>();
                foreach (var (key, list) in _byKey)
                {
                    if (list.Count == 0) 
                    { 
                        emptyKeys.Add(key); 
                        continue; 
                    }
                    if (list[^1].Timestamp.UtcTicks < f) continue;
                    if (list[0].Timestamp.UtcTicks > t) continue;

                    int i0 = LowerBound(list, f);
                    int i1 = UpperExclusive(list, t);
                    int remove = Math.Max(0, i1 - i0);
                    if (remove > 0)
                    {
                        list.RemoveRange(i0, remove);
                        _count -= remove;
                    }
                    if (list.Count == 0) 
                        emptyKeys.Add(key);
                }

                foreach (var k in emptyKeys) 
                    _byKey.Remove(k);
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<TemporalItem<KeyValuePair<TKey, TValue>>> GetBefore(DateTimeOffset time)
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return [];
                long c = time.UtcTicks;

                var acc = new List<TemporalItem<KeyValuePair<TKey, TValue>>>();
                foreach (var list in _byKey.Values)
                {
                    if (list.Count == 0) continue;
                    if (list[0].Timestamp.UtcTicks >= c) continue;

                    int end = LowerBound(list, c); // [0..end-1] are < c
                    for (int i = 0; i < end; i++) 
                        acc.Add(list[i]);
                }

                if (acc.Count <= 1) 
                    return acc.ToArray();
                acc.Sort((a, b) => a.Timestamp.UtcTicks.CompareTo(b.Timestamp.UtcTicks));
                return acc.ToArray();
            }
        }

        /// <inheritdoc/>
        public override IEnumerable<TemporalItem<KeyValuePair<TKey, TValue>>> GetAfter(DateTimeOffset time)
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return [];
                long c = time.UtcTicks;

                var acc = new List<TemporalItem<KeyValuePair<TKey, TValue>>>();
                foreach (var list in _byKey.Values)
                {
                    if (list.Count == 0) continue;
                    if (list[^1].Timestamp.UtcTicks <= c) continue;

                    int start = UpperExclusive(list, c); // first > c
                    for (int i = start; i < list.Count; i++) 
                        acc.Add(list[i]);
                }

                if (acc.Count <= 1)
                    return acc.ToArray();
                acc.Sort((a, b) => a.Timestamp.UtcTicks.CompareTo(b.Timestamp.UtcTicks));
                return acc.ToArray();
            }
        }

        /// <inheritdoc/>
        public override int CountSince(DateTimeOffset from)
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return 0;
                long f = from.UtcTicks;

                int total = 0;
                foreach (var list in _byKey.Values)
                {
                    if (list.Count == 0) continue;
                    if (list[^1].Timestamp.UtcTicks < f) continue;

                    int idx = LowerBound(list, f); // first >= f
                    total += list.Count - idx;
                }
                return total;
            }
        }

        /// <inheritdoc/>
        public override TemporalItem<KeyValuePair<TKey, TValue>>? GetNearest(DateTimeOffset time)
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return null;

                long target = time.UtcTicks;
                TemporalItem<KeyValuePair<TKey, TValue>>? best = null;
                long bestDiff = long.MaxValue;

                foreach (var list in _byKey.Values)
                {
                    if (list.Count == 0) continue;

                    int idx = LowerBound(list, target);

                    if (idx < list.Count)
                    {
                        var cand = list[idx];
                        long diff = cand.Timestamp.UtcTicks - target;
                        if (diff < 0) diff = -diff;
                        if (diff < bestDiff ||
                            (diff == bestDiff && cand.Timestamp.UtcTicks < (best?.Timestamp.UtcTicks ?? long.MaxValue)))
                        {
                            best = cand;
                            bestDiff = diff;
                        }
                    }

                    if (idx > 0)
                    {
                        var cand = list[idx - 1];
                        long diff = target - cand.Timestamp.UtcTicks;
                        if (diff < 0) diff = -diff;
                        if (diff < bestDiff ||
                            (diff == bestDiff && cand.Timestamp.UtcTicks < (best?.Timestamp.UtcTicks ?? long.MaxValue)))
                        {
                            best = cand;
                            bestDiff = diff;
                        }
                    }
                }

                return best;
            }
        }

        /// <inheritdoc/>
        public override TimeSpan GetTimeSpan()
        {
            lock (_lock)
            {
                if (_count <= 1) 
                    return TimeSpan.Zero;

                DateTimeOffset? min = null, max = null;
                foreach (var list in _byKey.Values)
                {
                    if (list.Count == 0) continue;
                    var first = list[0].Timestamp;
                    var last = list[^1].Timestamp;

                    if (min is null || first < min) min = first;
                    if (max is null || last > max) max = last;
                }

                if (min is null || max is null || min.Value >= max.Value) 
                    return TimeSpan.Zero;
                return max.Value - min.Value;
            }
        }

        /// <inheritdoc/>
        public override TemporalItem<KeyValuePair<TKey, TValue>>? GetLatest()
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return null;

                TemporalItem<KeyValuePair<TKey, TValue>>? latest = null;
                foreach (var list in _byKey.Values)
                {
                    if (list.Count == 0) continue;
                    var last = list[^1];
                    if (latest is null || last.Timestamp.UtcTicks > latest.Timestamp.UtcTicks)
                        latest = last;
                }
                return latest;
            }
        }

        /// <inheritdoc/>
        public override TemporalItem<KeyValuePair<TKey, TValue>>? GetEarliest()
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return null;

                TemporalItem<KeyValuePair<TKey, TValue>>? earliest = null;
                foreach (var list in _byKey.Values)
                {
                    if (list.Count == 0) continue;
                    var first = list[0];
                    if (earliest is null || first.Timestamp.UtcTicks < earliest.Timestamp.UtcTicks)
                        earliest = first;
                }
                return earliest;
            }
        }

        // ---------- Private helpers ----------

        /// <summary>Per-list lower bound: first index with timestamp ticks &gt;= <paramref name="ticks"/>.</summary>
        private static int LowerBound(List<TemporalItem<KeyValuePair<TKey, TValue>>> list, long ticks)
        {
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                long m = list[mid].Timestamp.UtcTicks;
                if (m < ticks) 
                    lo = mid + 1; 
                else hi = mid;
            }
            return lo;
        }

        /// <summary>Per-list upper exclusive: first index with timestamp ticks &gt; <paramref name="ticks"/>.</summary>
        private static int UpperExclusive(List<TemporalItem<KeyValuePair<TKey, TValue>>> list, long ticks)
        {
            int lo = 0, hi = list.Count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                long m = list[mid].Timestamp.UtcTicks;
                if (m <= ticks) 
                    lo = mid + 1; 
                else hi = mid;
            }
            return lo;
        }
    }
}