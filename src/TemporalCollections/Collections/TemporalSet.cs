// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using System.Collections.Concurrent;
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe temporal set where each unique item has an associated insertion timestamp.
    /// Supports time-based queries and removal of old items via <see cref="ITimeQueryable{T}"/>.
    /// Public API uses DateTime; internal comparisons use DateTimeOffset (UTC).
    /// </summary>
    /// <typeparam name="T">Type of items stored in the set; must be non-nullable.</typeparam>
    public class TemporalSet<T> : TimeQueryableBase<T> where T : notnull
    {
        private readonly ConcurrentDictionary<T, TemporalItem<T>> _dict;

        /// <summary>
        /// Creates a new temporal set. An optional equality comparer can be supplied to
        /// customize item equality (e.g., case-insensitive strings).
        /// </summary>
        /// <param name="comparer">Equality comparer for items; defaults to <see cref="EqualityComparer{T}.Default"/>.</param>
        public TemporalSet(IEqualityComparer<T>? comparer = null)
        {
            _dict = new ConcurrentDictionary<T, TemporalItem<T>>(comparer ?? EqualityComparer<T>.Default);
        }

        /// <summary>
        /// Adds an item to the set with the current timestamp if not already present.
        /// </summary>
        public bool Add(T item)
        {
            var temporalItem = TemporalItem<T>.Create(item); // Timestamp is DateTimeOffset UTC (monotonic)
            return _dict.TryAdd(item, temporalItem);
        }

        /// <summary>
        /// Checks whether the set contains the specified item.
        /// </summary>
        public bool Contains(T item) => _dict.ContainsKey(item);

        /// <summary>
        /// Removes the specified item from the set.
        /// </summary>
        public bool Remove(T item) => _dict.TryRemove(item, out _);

        /// <summary>
        /// Returns all temporal items whose timestamps fall within the specified inclusive range.
        /// </summary>
        public override IEnumerable<TemporalItem<T>> GetInRange(DateTimeOffset from, DateTimeOffset to)
        {
            long f = from.UtcTicks, t = to.UtcTicks;
            if (f > t) 
                (f, t) = (t, f);

            return _dict.Values
                .Where(i => f <= i.Timestamp.UtcTicks && i.Timestamp.UtcTicks <= t)
                .OrderBy(i => i.Timestamp.UtcTicks)
                .ToList();
        }

        /// <summary>
        /// Removes all items whose timestamps are older than the specified cutoff date (strictly less).
        /// </summary>
        public override void RemoveOlderThan(DateTimeOffset cutoff)
        {
            long c = cutoff.UtcTicks;

            foreach (var kv in _dict)
            {
                if (kv.Value.Timestamp.UtcTicks < c)
                {
                    _dict.TryRemove(kv.Key, out _);
                }
            }
        }

        /// <summary>
        /// Gets a snapshot of all temporal items currently in the set (ordered ascending by timestamp).
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetItems()
        {
            return _dict.Values
                .OrderBy(i => i.Timestamp.UtcTicks)
                .ToList();
        }

        /// <summary>
        /// Calculates the time span between the earliest and latest items in the set.
        /// </summary>
        public override TimeSpan GetTimeSpan()
        {
            var snapshot = _dict.Values;
            using var e = snapshot.GetEnumerator();
            if (!e.MoveNext()) return TimeSpan.Zero;

            long min = e.Current.Timestamp.UtcTicks;
            long max = min;

            while (e.MoveNext())
            {
                long x = e.Current.Timestamp.UtcTicks;
                if (x < min) min = x;
                else if (x > max) max = x;
            }

            long delta = max - min;
            return delta > 0 ? TimeSpan.FromTicks(delta) : TimeSpan.Zero;
        }

        /// <summary>
        /// Counts the number of items whose timestamps fall within the specified inclusive time range.
        /// </summary>
        public override int CountInRange(DateTimeOffset from, DateTimeOffset to)
        {
            long f = from.UtcTicks, t = to.UtcTicks;
            if (f > t) 
                (f, t) = (t, f);

            int count = 0;
            foreach (var ti in _dict.Values)
            {
                long x = ti.Timestamp.UtcTicks;
                if (f <= x && x <= t) count++;
            }
            return count;
        }

        /// <summary>
        /// Removes all items from the set.
        /// </summary>
        public override void Clear() => _dict.Clear();

        /// <summary>
        /// Removes all items whose timestamps fall within the specified inclusive time range.
        /// </summary>
        public override void RemoveRange(DateTimeOffset from, DateTimeOffset to)
        {
            long f = from.UtcTicks, t = to.UtcTicks;
            if (f > t) 
                (f, t) = (t, f);

            foreach (var kv in _dict)
            {
                long x = kv.Value.Timestamp.UtcTicks;
                if (f <= x && x <= t)
                    _dict.TryRemove(kv.Key, out _);
            }
        }

        /// <summary>
        /// Gets the temporal item with the latest timestamp, or null if empty.
        /// </summary>
        public override TemporalItem<T>? GetLatest()
        {
            if (_dict.IsEmpty) return null;
            // MaxBy on ticks to be explicit and consistent
            return _dict.Values.MaxBy(ti => ti.Timestamp.UtcTicks);
        }

        /// <summary>
        /// Gets the temporal item with the earliest timestamp, or null if empty.
        /// </summary>
        public override TemporalItem<T>? GetEarliest()
        {
            if (_dict.IsEmpty) return null;
            return _dict.Values.MinBy(ti => ti.Timestamp.UtcTicks);
        }

        /// <summary>
        /// Gets all items whose timestamps are strictly earlier than the specified time.
        /// </summary>
        public override IEnumerable<TemporalItem<T>> GetBefore(DateTimeOffset time)
        {
            long cutoff = time.UtcTicks;

            return _dict.Values
                .Where(i => i.Timestamp.UtcTicks < cutoff)
                .OrderBy(i => i.Timestamp.UtcTicks)
                .ToList();
        }

        /// <summary>
        /// Gets all items whose timestamps are strictly later than the specified time.
        /// </summary>
        public override IEnumerable<TemporalItem<T>> GetAfter(DateTimeOffset time)
        {
            long cutoff = time.UtcTicks;

            return _dict.Values
                .Where(i => i.Timestamp.UtcTicks > cutoff)
                .OrderBy(i => i.Timestamp.UtcTicks)
                .ToList();
        }

        /// <summary>
        /// Counts the number of items with timestamp greater than or equal to the specified cutoff.
        /// </summary>
        public override int CountSince(DateTimeOffset from)
        {
            long f = from.UtcTicks;
            int count = 0;

            foreach (var item in _dict.Values)
            {
                if (item.Timestamp.UtcTicks >= f)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Returns the item whose timestamp is closest to <paramref name="time"/>.
        /// If the set is empty, returns <c>null</c>.
        /// In case of a tie (same distance before/after), the later item (timestamp ≥ time) is returned.
        /// Complexity: O(n).
        /// </summary>
        public override TemporalItem<T>? GetNearest(DateTimeOffset time)
        {
            if (_dict.IsEmpty) return null;

            long target = time.UtcTicks;

            TemporalItem<T>? best = null;
            long bestDiff = long.MaxValue;

            foreach (var item in _dict.Values)
            {
                long ticks = item.Timestamp.UtcTicks;
                long diff = ticks >= target ? (ticks - target) : (target - ticks);

                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = item;
                }
                else if (diff == bestDiff && best is not null)
                {
                    // Tie-break: prefer the later item (>= time)
                    bool itemIsAfterOrEqual = ticks >= target;
                    bool bestIsAfterOrEqual = best.Timestamp.UtcTicks >= target;

                    if (itemIsAfterOrEqual && !bestIsAfterOrEqual)
                    {
                        best = item;
                    }
                    else if (itemIsAfterOrEqual == bestIsAfterOrEqual && ticks > best.Timestamp.UtcTicks)
                    {
                        best = item; // determinism: pick the later one if on the same side
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Returns the number of items currently in the set.
        /// </summary>
        public int Count => _dict.Count;

        /// <summary>
        /// Indicates whether the set is currently empty.
        /// This check is O(1) and thread-safe, as it relies on the underlying
        /// <see cref="ConcurrentDictionary{TKey, TValue}.IsEmpty"/> property.
        /// </summary>
        public bool IsEmpty => _dict.IsEmpty;
    }
}