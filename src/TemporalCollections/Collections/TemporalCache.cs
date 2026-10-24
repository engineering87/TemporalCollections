// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using System.Collections.Concurrent;
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// Thread-safe key/value cache where each entry is timestamped at write time and expires
    /// after a time-to-live (TTL). Supports absolute expiration (default) or sliding expiration
    /// (each successful read extends the entry lifetime).
    /// Expired entries are never returned; they are removed lazily on access or eagerly via <see cref="RemoveExpired"/>.
    /// Implements <see cref="ITimeQueryable{T}"/> over live entries, using the write timestamp.
    /// </summary>
    /// <typeparam name="TKey">The type of keys (not nullable).</typeparam>
    /// <typeparam name="TValue">The type of cached values.</typeparam>
    public class TemporalCache<TKey, TValue> : TimeQueryableBase<KeyValuePair<TKey, TValue>>
        where TKey : notnull
    {
        private sealed class Entry
        {
            public Entry(TemporalItem<KeyValuePair<TKey, TValue>> item, TimeSpan ttl, long expiresAtTicks)
            {
                Item = item;
                Ttl = ttl;
                ExpiresAtTicks = expiresAtTicks;
            }

            public TemporalItem<KeyValuePair<TKey, TValue>> Item { get; }
            public TimeSpan Ttl { get; }
            public long ExpiresAtTicks { get; }
        }

        private readonly ConcurrentDictionary<TKey, Entry> _dict;
        private readonly TimeProvider _timeProvider;

        /// <summary>
        /// Initializes a new cache.
        /// </summary>
        /// <param name="defaultTtl">Default time-to-live for entries. Must be positive.</param>
        /// <param name="slidingExpiration">If true, each successful read resets the entry lifetime.</param>
        /// <param name="timeProvider">Clock used for expiration checks. Defaults to <see cref="TimeProvider.System"/>.</param>
        /// <param name="comparer">Optional key comparer.</param>
        public TemporalCache(
            TimeSpan defaultTtl,
            bool slidingExpiration = false,
            TimeProvider? timeProvider = null,
            IEqualityComparer<TKey>? comparer = null)
        {
            ValidateTtl(defaultTtl, nameof(defaultTtl));
            DefaultTtl = defaultTtl;
            SlidingExpiration = slidingExpiration;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _dict = new ConcurrentDictionary<TKey, Entry>(comparer ?? EqualityComparer<TKey>.Default);
        }

        /// <summary>Default time-to-live applied when no explicit TTL is specified.</summary>
        public TimeSpan DefaultTtl { get; }

        /// <summary>Whether reads extend the entry lifetime.</summary>
        public bool SlidingExpiration { get; }

        /// <summary>Number of live (non-expired) entries.</summary>
        public int Count
        {
            get
            {
                long now = NowTicks;
                int count = 0;
                foreach (var kvp in _dict)
                    if (!IsExpired(kvp.Value, now)) count++;
                return count;
            }
        }

        /// <summary>Snapshot of keys of live (non-expired) entries.</summary>
        public IReadOnlyList<TKey> Keys
        {
            get
            {
                long now = NowTicks;
                return _dict.Where(kvp => !IsExpired(kvp.Value, now)).Select(kvp => kvp.Key).ToList();
            }
        }

        /// <summary>Adds or replaces the value for <paramref name="key"/> using the default TTL.</summary>
        public void Set(TKey key, TValue value) => Set(key, value, DefaultTtl);

        /// <summary>Adds or replaces the value for <paramref name="key"/> using the specified TTL.</summary>
        public void Set(TKey key, TValue value, TimeSpan ttl)
        {
            ValidateTtl(ttl, nameof(ttl));
            _dict[key] = CreateEntry(key, value, ttl);
        }

        /// <summary>
        /// Adds the entry only if the key is absent or its current entry is expired.
        /// Returns true if added.
        /// </summary>
        public bool TryAdd(TKey key, TValue value) => TryAdd(key, value, DefaultTtl);

        /// <summary>
        /// Adds the entry with the specified TTL only if the key is absent or its current entry is expired.
        /// Returns true if added.
        /// </summary>
        public bool TryAdd(TKey key, TValue value, TimeSpan ttl)
        {
            ValidateTtl(ttl, nameof(ttl));
            while (true)
            {
                var entry = CreateEntry(key, value, ttl);
                if (_dict.TryAdd(key, entry))
                    return true;

                if (!_dict.TryGetValue(key, out var existing))
                    continue;

                if (!IsExpired(existing, NowTicks))
                    return false;

                if (_dict.TryUpdate(key, entry, existing))
                    return true;
            }
        }

        /// <summary>
        /// Returns the existing live value for <paramref name="key"/>, or adds the value produced by
        /// <paramref name="valueFactory"/> using the default TTL.
        /// </summary>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory) => GetOrAdd(key, valueFactory, DefaultTtl);

        /// <summary>
        /// Returns the existing live value for <paramref name="key"/>, or adds the value produced by
        /// <paramref name="valueFactory"/> using the specified TTL.
        /// The factory may be invoked more than once under contention; only one result is stored.
        /// </summary>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory, TimeSpan ttl)
        {
            ArgumentNullException.ThrowIfNull(valueFactory);
            ValidateTtl(ttl, nameof(ttl));

            while (true)
            {
                if (TryGetValue(key, out var existing))
                    return existing;

                var entry = CreateEntry(key, valueFactory(key), ttl);

                if (_dict.TryAdd(key, entry))
                    return entry.Item.Value.Value;

                if (_dict.TryGetValue(key, out var current) && IsExpired(current, NowTicks)
                    && _dict.TryUpdate(key, entry, current))
                    return entry.Item.Value.Value;
            }
        }

        /// <summary>
        /// Attempts to get the live value for <paramref name="key"/>.
        /// With sliding expiration, a successful read extends the entry lifetime.
        /// </summary>
        public bool TryGetValue(TKey key, out TValue value)
        {
            while (_dict.TryGetValue(key, out var entry))
            {
                long now = NowTicks;
                if (IsExpired(entry, now))
                {
                    RemoveIfSame(key, entry);
                    break;
                }

                if (SlidingExpiration)
                {
                    var refreshed = new Entry(entry.Item, entry.Ttl, AddTicks(now, entry.Ttl));
                    if (!_dict.TryUpdate(key, refreshed, entry))
                        continue; // concurrently modified; re-read
                }

                value = entry.Item.Value.Value;
                return true;
            }

            value = default!;
            return false;
        }

        /// <summary>
        /// Attempts to get the live temporal item for <paramref name="key"/> without extending its lifetime.
        /// </summary>
        public bool TryGetItem(TKey key, out TemporalItem<KeyValuePair<TKey, TValue>>? item)
        {
            if (_dict.TryGetValue(key, out var entry) && !IsExpired(entry, NowTicks))
            {
                item = entry.Item;
                return true;
            }
            item = null;
            return false;
        }

        /// <summary>Returns true if a live entry exists for <paramref name="key"/> (does not extend lifetime).</summary>
        public bool ContainsKey(TKey key)
            => _dict.TryGetValue(key, out var entry) && !IsExpired(entry, NowTicks);

        /// <summary>Removes the entry for <paramref name="key"/>. Returns true only if a live entry was removed.</summary>
        public bool TryRemove(TKey key, out TValue value)
        {
            if (_dict.TryRemove(key, out var entry) && !IsExpired(entry, NowTicks))
            {
                value = entry.Item.Value.Value;
                return true;
            }
            value = default!;
            return false;
        }

        /// <summary>
        /// Returns the remaining lifetime of the entry for <paramref name="key"/>, or null if missing/expired.
        /// </summary>
        public TimeSpan? GetTimeToLive(TKey key)
        {
            if (!_dict.TryGetValue(key, out var entry)) return null;
            long now = NowTicks;
            if (IsExpired(entry, now)) return null;
            return TimeSpan.FromTicks(entry.ExpiresAtTicks - now);
        }

        /// <summary>
        /// Resets the lifetime of a live entry to its TTL, starting from now. Returns false if missing/expired.
        /// </summary>
        public bool Touch(TKey key)
        {
            while (_dict.TryGetValue(key, out var entry))
            {
                long now = NowTicks;
                if (IsExpired(entry, now)) return false;
                var refreshed = new Entry(entry.Item, entry.Ttl, AddTicks(now, entry.Ttl));
                if (_dict.TryUpdate(key, refreshed, entry)) return true;
            }
            return false;
        }

        /// <summary>Removes all expired entries. Returns the number of entries removed.</summary>
        public int RemoveExpired()
        {
            long now = NowTicks;
            int removed = 0;
            foreach (var kvp in _dict)
            {
                if (IsExpired(kvp.Value, now) && RemoveIfSame(kvp.Key, kvp.Value))
                    removed++;
            }
            return removed;
        }

        #region ITimeQueryable<KeyValuePair<TKey,TValue>> implementation

        /// <summary>Returns live items written within [from, to] (inclusive), ordered by timestamp.</summary>
        public override IEnumerable<TemporalItem<KeyValuePair<TKey, TValue>>> GetInRange(DateTimeOffset from, DateTimeOffset to)
        {
            long f = from.UtcTicks, t = to.UtcTicks;
            if (f > t)
                (f, t) = (t, f);
            return Snapshot(x => f <= x && x <= t);
        }

        /// <summary>Removes all entries written strictly before <paramref name="cutoff"/>.</summary>
        public override void RemoveOlderThan(DateTimeOffset cutoff)
        {
            long c = cutoff.UtcTicks;
            RemoveWhere(x => x < c);
        }

        /// <summary>Counts live items written within [from, to] (inclusive).</summary>
        public override int CountInRange(DateTimeOffset from, DateTimeOffset to)
        {
            long f = from.UtcTicks, t = to.UtcTicks;
            if (f > t)
                (f, t) = (t, f);
            return CountWhere(x => f <= x && x <= t);
        }

        /// <summary>Removes all entries written within [from, to] (inclusive).</summary>
        public override void RemoveRange(DateTimeOffset from, DateTimeOffset to)
        {
            long f = from.UtcTicks, t = to.UtcTicks;
            if (f > t)
                (f, t) = (t, f);
            RemoveWhere(x => f <= x && x <= t);
        }

        /// <summary>Returns live items written strictly before <paramref name="time"/>.</summary>
        public override IEnumerable<TemporalItem<KeyValuePair<TKey, TValue>>> GetBefore(DateTimeOffset time)
        {
            long t = time.UtcTicks;
            return Snapshot(x => x < t);
        }

        /// <summary>Returns live items written strictly after <paramref name="time"/>.</summary>
        public override IEnumerable<TemporalItem<KeyValuePair<TKey, TValue>>> GetAfter(DateTimeOffset time)
        {
            long t = time.UtcTicks;
            return Snapshot(x => x > t);
        }

        /// <summary>Counts live items written at or after <paramref name="from"/>.</summary>
        public override int CountSince(DateTimeOffset from)
        {
            long f = from.UtcTicks;
            return CountWhere(x => x >= f);
        }

        /// <summary>Returns the live item whose write timestamp is closest to <paramref name="time"/>.</summary>
        public override TemporalItem<KeyValuePair<TKey, TValue>>? GetNearest(DateTimeOffset time)
        {
            long t = time.UtcTicks;
            long now = NowTicks;
            TemporalItem<KeyValuePair<TKey, TValue>>? best = null;
            ulong bestDiff = ulong.MaxValue;

            foreach (var kvp in _dict)
            {
                if (IsExpired(kvp.Value, now)) continue;
                var item = kvp.Value.Item;
                long x = item.Timestamp.UtcTicks;
                ulong diff = x >= t ? (ulong)(x - t) : (ulong)(t - x);
                if (best is null || diff < bestDiff || (diff == bestDiff && x < best.Timestamp.UtcTicks))
                {
                    best = item;
                    bestDiff = diff;
                }
            }
            return best;
        }

        /// <summary>Returns latest - earliest write timestamp among live items, or zero if fewer than two.</summary>
        public override TimeSpan GetTimeSpan()
        {
            var earliest = GetEarliest();
            var latest = GetLatest();
            if (earliest is null || latest is null) return TimeSpan.Zero;
            var span = latest.Timestamp - earliest.Timestamp;
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }

        /// <summary>Returns the most recently written live item, or null if empty.</summary>
        public override TemporalItem<KeyValuePair<TKey, TValue>>? GetLatest()
        {
            long now = NowTicks;
            TemporalItem<KeyValuePair<TKey, TValue>>? best = null;
            foreach (var kvp in _dict)
            {
                if (IsExpired(kvp.Value, now)) continue;
                var item = kvp.Value.Item;
                if (best is null || item.Timestamp.UtcTicks > best.Timestamp.UtcTicks) best = item;
            }
            return best;
        }

        /// <summary>Returns the earliest written live item, or null if empty.</summary>
        public override TemporalItem<KeyValuePair<TKey, TValue>>? GetEarliest()
        {
            long now = NowTicks;
            TemporalItem<KeyValuePair<TKey, TValue>>? best = null;
            foreach (var kvp in _dict)
            {
                if (IsExpired(kvp.Value, now)) continue;
                var item = kvp.Value.Item;
                if (best is null || item.Timestamp.UtcTicks < best.Timestamp.UtcTicks) best = item;
            }
            return best;
        }

        /// <summary>Removes all entries.</summary>
        public override void Clear() => _dict.Clear();

        #endregion

        private long NowTicks => _timeProvider.GetUtcNow().UtcTicks;

        private static bool IsExpired(Entry entry, long nowTicks) => entry.ExpiresAtTicks <= nowTicks;

        private static long AddTicks(long now, TimeSpan ttl)
        {
            long max = DateTimeOffset.MaxValue.UtcTicks;
            return ttl.Ticks >= max - now ? max : now + ttl.Ticks;
        }

        private static void ValidateTtl(TimeSpan ttl, string paramName)
        {
            if (ttl <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(paramName, "TTL must be positive.");
        }

        private Entry CreateEntry(TKey key, TValue value, TimeSpan ttl)
        {
            var item = TemporalItem<KeyValuePair<TKey, TValue>>.Create(new KeyValuePair<TKey, TValue>(key, value), _timeProvider);
            return new Entry(item, ttl, AddTicks(NowTicks, ttl));
        }

        private bool RemoveIfSame(TKey key, Entry entry)
            => ((ICollection<KeyValuePair<TKey, Entry>>)_dict).Remove(new KeyValuePair<TKey, Entry>(key, entry));

        private List<TemporalItem<KeyValuePair<TKey, TValue>>> Snapshot(Func<long, bool> predicate)
        {
            long now = NowTicks;
            var result = new List<TemporalItem<KeyValuePair<TKey, TValue>>>();
            foreach (var kvp in _dict)
            {
                if (IsExpired(kvp.Value, now)) continue;
                var item = kvp.Value.Item;
                if (predicate(item.Timestamp.UtcTicks)) result.Add(item);
            }
            result.Sort(TemporalItem<KeyValuePair<TKey, TValue>>.TimestampComparer);
            return result;
        }

        private int CountWhere(Func<long, bool> predicate)
        {
            long now = NowTicks;
            int count = 0;
            foreach (var kvp in _dict)
            {
                if (!IsExpired(kvp.Value, now) && predicate(kvp.Value.Item.Timestamp.UtcTicks)) count++;
            }
            return count;
        }

        private void RemoveWhere(Func<long, bool> predicate)
        {
            foreach (var kvp in _dict)
            {
                if (predicate(kvp.Value.Item.Timestamp.UtcTicks))
                    RemoveIfSame(kvp.Key, kvp.Value);
            }
        }
    }
}
