// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using System.Collections.Concurrent;
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe dictionary storing multiple timestamped values per key,
    /// supporting temporal range queries and cleanup of old entries.
    /// Implements ITimeQueryable to query over all keys.
    /// Public API uses DateTime; internal comparisons use DateTimeOffset (UTC).
    /// </summary>
    /// <typeparam name="TKey">The type of keys in the dictionary (not nullable).</typeparam>
    /// <typeparam name="TValue">The type of values stored with timestamps.</typeparam>
    public class TemporalDictionary<TKey, TValue> : TimeQueryableBase<KeyValuePair<TKey, TValue>>
        where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, List<TemporalItem<TValue>>> _dict = new();

        /// <summary>
        /// Adds a new value associated with the specified key, timestamped with the current UTC time.
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            var temporalItem = TemporalItem<TValue>.Create(value);

            // Retry loop: if a concurrent RemoveOlderThan/RemoveRange removes the key
            // between GetOrAdd and lock(list), the list becomes orphaned. We detect this
            // by verifying the list is still the one in the dictionary after acquiring the lock.
            while (true)
            {
                var list = _dict.GetOrAdd(key, _ => []);

                lock (list)
                {
                    // Verify this list is still the one associated with the key.
                    if (_dict.TryGetValue(key, out var current) && ReferenceEquals(current, list))
                    {
                        // Because timestamps are strictly increasing across all TemporalItem<TValue>
                        // instances created via TemporalItem<T>.Create, and we never insert items
                        // with arbitrary timestamps, we know that:
                        //
                        //   - For each key, the sequence of timestamps in "list" is strictly increasing.
                        //   - The new "temporalItem" always has the largest (most recent) timestamp.
                        //
                        // Therefore, we can safely append to the end of the list while preserving
                        // the sorted-by-timestamp invariant. No BinarySearch + Insert is needed.
                        list.Add(temporalItem);
                        return;
                    }
                }
                // List was removed concurrently; retry with a fresh GetOrAdd.
            }
        }

        /// <summary>
        /// Retrieves all temporal items associated with the specified <paramref name="key"/> whose timestamps
        /// fall within the inclusive range from <paramref name="from"/> to <paramref name="to"/>.
        /// </summary>
        public IEnumerable<TemporalItem<TValue>> GetInRange(TKey key, DateTimeOffset from, DateTimeOffset to)
        {
            var f = from.UtcTicks;
            var t = to.UtcTicks;
            if (f > t) 
                (f, t) = (t, f);

            if (!_dict.TryGetValue(key, out var list)) yield break;

            List<TemporalItem<TValue>> snapshot;
            lock (list)
            {
                if (list.Count == 0) yield break;

                int lo = LowerBound(list, f);
                int hi = UpperBound(list, t) - 1;
                if (lo > hi) yield break;

                int count = hi - lo + 1;
                snapshot = new List<TemporalItem<TValue>>(count);
                for (int i = lo; i <= hi; i++)
                    snapshot.Add(list[i]);
            }

            foreach (var item in snapshot)
                yield return item;
        }

        /// <summary>
        /// Removes all timestamped values older than the specified cutoff date from all keys.
        /// </summary>
        public override void RemoveOlderThan(DateTimeOffset cutoff)
        {
            long c = cutoff.UtcTicks;

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    // Binary search: find first index with ts >= cutoff, then remove [0, idx) in one shot
                    int idx = LowerBound(list, c);
                    if (idx > 0)
                        list.RemoveRange(0, idx);

                    if (list.Count == 0)
                        _dict.TryRemove(kvp.Key, out _);
                }
            }
        }

        /// <summary>
        /// Gets all keys currently present in the dictionary.
        /// </summary>
        public IEnumerable<TKey> Keys => _dict.Keys;

        /// <summary>
        /// Gets the number of keys currently stored in the dictionary.
        /// </summary>
        public int Count => _dict.Count;

        #region ITimeQueryable<KeyValuePair<TKey,TValue>> implementation

        /// <summary>
        /// Retrieves all temporal items stored in the dictionary whose timestamps
        /// fall within the inclusive range from <paramref name="from"/> to <paramref name="to"/>.
        /// Each item returned is wrapped as a <see cref="TemporalItem{T}"/> containing
        /// a <see cref="KeyValuePair{TKey, TValue}"/> with the original key and value.
        /// </summary>
        public override IEnumerable<TemporalItem<KeyValuePair<TKey, TValue>>> GetInRange(DateTimeOffset from, DateTimeOffset to)
        {
            long f = from.UtcTicks, t = to.UtcTicks;
            if (f > t) 
                (f, t) = (t, f);

            var results = new List<TemporalItem<KeyValuePair<TKey, TValue>>>();

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    int n = list.Count;
                    if (n == 0) continue;

                    // Binary search: skip elements outside [f, t] in O(log n)
                    int lo = LowerBound(list, f);
                    int hi = UpperBound(list, t) - 1;

                    for (int i = lo; i <= hi; i++)
                    {
                        var item = list[i];
                        var pair = new KeyValuePair<TKey, TValue>(kvp.Key, item.Value);
                        results.Add(new TemporalItem<KeyValuePair<TKey, TValue>>(pair, item.Timestamp));
                    }
                }
            }

            // Ensure deterministic ordering across keys
            results.Sort((a, b) => a.Timestamp.UtcTicks.CompareTo(b.Timestamp.UtcTicks));
            return results;
        }

        #endregion

        /// <summary>
        /// Returns the time span between the earliest and the latest timestamp across all stored items.
        /// Returns <see cref="TimeSpan.Zero"/> if the dictionary is empty.
        /// </summary>
        public override TimeSpan GetTimeSpan()
        {
            bool any = false;
            DateTimeOffset min = DateTimeOffset.MaxValue;
            DateTimeOffset max = DateTimeOffset.MinValue;

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    foreach (var it in list)
                    {
                        any = true;
                        var ts = it.Timestamp;
                        if (ts < min) min = ts;
                        if (ts > max) max = ts;
                    }
                }
            }

            if (!any || min >= max) return TimeSpan.Zero;
            var span = max - min; // DateTimeOffset subtraction → TimeSpan
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }

        /// <summary>
        /// Counts how many items across all keys have a timestamp within the inclusive range
        /// from <paramref name="from"/> to <paramref name="to"/>.
        /// </summary>
        public override int CountInRange(DateTimeOffset from, DateTimeOffset to)
        {
            long f = from.UtcTicks, t = to.UtcTicks;
            if (f > t) 
                (f, t) = (t, f);

            var count = 0;
            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    if (list.Count == 0) continue;
                    // Binary search: count = UpperBound(t) - LowerBound(f)
                    int lo = LowerBound(list, f);
                    int hi = UpperBound(list, t);
                    count += hi - lo;
                }
            }
            return count;
        }

        /// <summary>
        /// Removes all keys and all their timestamped values from the dictionary.
        /// </summary>
        public override void Clear() => _dict.Clear();

        /// <summary>
        /// Removes all items whose timestamps fall within the inclusive range
        /// from <paramref name="from"/> to <paramref name="to"/> across all keys.
        /// Keys left with no items are removed as well.
        /// </summary>
        public override void RemoveRange(DateTimeOffset from, DateTimeOffset to)
        {
            long f = from.UtcTicks, t = to.UtcTicks;
            if (f > t)
                (f, t) = (t, f);

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    if (list.Count == 0) continue;
                    // The list is sorted ascending: items in [f, t] form a contiguous segment
                    int lo = LowerBound(list, f);          // first index with ts >= f
                    int hi = UpperBound(list, t);          // first index with ts >  t
                    int removeCount = hi - lo;
                    if (removeCount > 0)
                        list.RemoveRange(lo, removeCount);

                    if (list.Count == 0)
                        _dict.TryRemove(kvp.Key, out _);
                }
            }
        }

        /// <summary>
        /// Returns the latest (most recent) item across all keys, or <c>null</c> if empty.
        /// The returned item contains the original key/value as a <see cref="KeyValuePair{TKey,TValue}"/>.
        /// </summary>
        public override TemporalItem<KeyValuePair<TKey, TValue>>? GetLatest()
        {
            DateTimeOffset bestTs = DateTimeOffset.MinValue;
            TKey? bestKey = default!;
            TValue? bestVal = default!;
            bool found = false;

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    if (list.Count == 0) continue;
                    // List is sorted ascending: last element is the latest
                    var it = list[^1];
                    if (!found || it.Timestamp.UtcTicks > bestTs.UtcTicks)
                    {
                        bestTs = it.Timestamp;
                        bestKey = kvp.Key;
                        bestVal = it.Value;
                        found = true;
                    }
                }
            }

            return found
                ? new TemporalItem<KeyValuePair<TKey, TValue>>(
                    new KeyValuePair<TKey, TValue>(bestKey!, bestVal!),
                    bestTs)
                : null;
        }

        /// <summary>
        /// Returns the earliest (oldest) item across all keys, or <c>null</c> if empty.
        /// The returned item contains the original key/value as a <see cref="KeyValuePair{TKey,TValue}"/>.
        /// </summary>
        public override TemporalItem<KeyValuePair<TKey, TValue>>? GetEarliest()
        {
            DateTimeOffset bestTs = DateTimeOffset.MaxValue;
            TKey? bestKey = default!;
            TValue? bestVal = default!;
            bool found = false;

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    if (list.Count == 0) continue;
                    // List is sorted ascending: first element is the earliest
                    var it = list[0];
                    if (!found || it.Timestamp.UtcTicks < bestTs.UtcTicks)
                    {
                        bestTs = it.Timestamp;
                        bestKey = kvp.Key;
                        bestVal = it.Value;
                        found = true;
                    }
                }
            }

            return found
                ? new TemporalItem<KeyValuePair<TKey, TValue>>(
                    new KeyValuePair<TKey, TValue>(bestKey!, bestVal!),
                    bestTs)
                : null;
        }

        /// <summary>
        /// Retrieves all items strictly before the specified <paramref name="time"/> across all keys.
        /// The returned items wrap the original key/value.
        /// </summary>
        public override IEnumerable<TemporalItem<KeyValuePair<TKey, TValue>>> GetBefore(DateTimeOffset time)
        {
            long cutoff = time.UtcTicks;
            var results = new List<TemporalItem<KeyValuePair<TKey, TValue>>>();

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    // Binary search: all elements before LowerBound(cutoff) have ts < cutoff
                    int hi = LowerBound(list, cutoff);
                    for (int i = 0; i < hi; i++)
                    {
                        var item = list[i];
                        results.Add(new TemporalItem<KeyValuePair<TKey, TValue>>(
                            new KeyValuePair<TKey, TValue>(kvp.Key, item.Value),
                            item.Timestamp));
                    }
                }
            }

            results.Sort((a, b) => a.Timestamp.UtcTicks.CompareTo(b.Timestamp.UtcTicks));
            return results;
        }

        /// <summary>
        /// Retrieves all items strictly after the specified <paramref name="time"/> across all keys.
        /// The returned items wrap the original key/value.
        /// </summary>
        public override IEnumerable<TemporalItem<KeyValuePair<TKey, TValue>>> GetAfter(DateTimeOffset time)
        {
            long cutoff = time.UtcTicks;
            var results = new List<TemporalItem<KeyValuePair<TKey, TValue>>>();

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    // Binary search: UpperBound(cutoff) is the first index with ts > cutoff
                    int lo = UpperBound(list, cutoff);
                    for (int i = lo; i < list.Count; i++)
                    {
                        var item = list[i];
                        results.Add(new TemporalItem<KeyValuePair<TKey, TValue>>(
                            new KeyValuePair<TKey, TValue>(kvp.Key, item.Value),
                            item.Timestamp));
                    }
                }
            }

            results.Sort((a, b) => a.Timestamp.UtcTicks.CompareTo(b.Timestamp.UtcTicks));
            return results;
        }

        /// <summary>
        /// Counts how many items across all keys have timestamp greater than or equal to the specified cutoff.
        /// </summary>
        public override int CountSince(DateTimeOffset from)
        {
            long f = from.UtcTicks;
            int count = 0;

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                lock (list)
                {
                    if (list.Count == 0) continue;
                    // All elements from LowerBound(f) to end have ts >= f
                    count += list.Count - LowerBound(list, f);
                }
            }

            return count;
        }

        /// <summary>
        /// Returns the item whose timestamp is closest to <paramref name="time"/> across all keys.
        /// If the dictionary is empty, returns <c>null</c>.
        /// In case of a tie (same distance before/after), the later item (timestamp ≥ time) is returned.
        /// Complexity: O(K log N_k) where K is the number of keys and N_k is the items per key.
        /// </summary>
        public override TemporalItem<KeyValuePair<TKey, TValue>>? GetNearest(DateTimeOffset time)
        {
            long target = time.UtcTicks;

            TemporalItem<KeyValuePair<TKey, TValue>>? best = null;
            long bestDiff = long.MaxValue;
            bool bestIsAfterOrEqual = false; // for tie-break
            long bestTicks = long.MinValue;   // secondary tie-break for determinism

            foreach (var kvp in _dict)
            {
                var list = kvp.Value;
                TemporalItem<TValue>? candBefore = null;
                TemporalItem<TValue>? candAfter = null;

                lock (list)
                {
                    int n = list.Count;
                    if (n == 0) continue;

                    int idx = LowerBound(list, target); // first with ts >= target
                    if (idx < n) candAfter = list[idx];
                    if (idx > 0) candBefore = list[idx - 1];
                }

                // Local compare helper
                void Consider(TemporalItem<TValue>? it)
                {
                    if (it is null) return;
                    long ticks = it.Timestamp.UtcTicks;
                    long diff = ticks >= target ? (ticks - target) : (target - ticks);
                    bool isAfterOrEqual = ticks >= target;

                    if (diff < bestDiff)
                    {
                        bestDiff = diff;
                        bestIsAfterOrEqual = isAfterOrEqual;
                        bestTicks = ticks;
                        best = new TemporalItem<KeyValuePair<TKey, TValue>>(
                            new KeyValuePair<TKey, TValue>(kvp.Key, it.Value),
                            it.Timestamp);
                    }
                    else if (diff == bestDiff && best is not null)
                    {
                        // Tie-break 1: prefer later item (>= target)
                        if (isAfterOrEqual && !bestIsAfterOrEqual)
                        {
                            bestIsAfterOrEqual = true;
                            bestTicks = ticks;
                            best = new TemporalItem<KeyValuePair<TKey, TValue>>(
                                new KeyValuePair<TKey, TValue>(kvp.Key, it.Value),
                                it.Timestamp);
                        }
                        // Tie-break 2: if both on same side, prefer the later timestamp for determinism
                        else if (isAfterOrEqual == bestIsAfterOrEqual && ticks > bestTicks)
                        {
                            bestTicks = ticks;
                            best = new TemporalItem<KeyValuePair<TKey, TValue>>(
                                new KeyValuePair<TKey, TValue>(kvp.Key, it.Value),
                                it.Timestamp);
                        }
                    }
                }

                Consider(candBefore);
                Consider(candAfter);
            }

            return best;
        }

        #region Internal helpers

        /// <summary>
        /// Finds the index of the first element in the list whose timestamp
        /// is greater than or equal to the specified <paramref name="ticks"/>.
        /// </summary>
        /// <param name="list">The sorted list of temporal items to search.</param>
        /// <param name="ticks">The UTC ticks (100-ns units) to compare against.</param>
        /// <returns>
        /// The zero-based index of the first element with <c>UtcTicks &gt;= ticks</c>;
        /// if all elements are smaller, returns <c>list.Count</c>.
        /// </returns>
        private static int LowerBound(List<TemporalItem<TValue>> list, long ticks)
        {
            int l = 0, r = list.Count;
            while (l < r)
            {
                int m = (l + r) >> 1;
                if (list[m].Timestamp.UtcTicks < ticks) l = m + 1;
                else r = m;
            }
            return l;
        }

        /// <summary>
        /// Finds the index of the first element in the list whose timestamp
        /// is strictly greater than the specified <paramref name="ticks"/>.
        /// </summary>
        /// <param name="list">The sorted list of temporal items to search.</param>
        /// <param name="ticks">The UTC ticks (100-ns units) to compare against.</param>
        /// <returns>
        /// The zero-based index of the first element with <c>UtcTicks &gt; ticks</c>;
        /// if no such element exists, returns <c>list.Count</c>.
        /// </returns>
        private static int UpperBound(List<TemporalItem<TValue>> list, long ticks)
        {
            int l = 0, r = list.Count;
            while (l < r)
            {
                int m = (l + r) >> 1;
                if (list[m].Timestamp.UtcTicks <= ticks) l = m + 1;
                else r = m;
            }
            return l;
        }

        #endregion
    }
}