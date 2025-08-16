// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using System.Collections.Concurrent;
using TemporalCollections.Abstractions;
using TemporalCollections.Models;
using TemporalCollections.Utilities;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe temporal set where each unique item has an associated insertion timestamp.
    /// Supports time-based queries and removal of old items via <see cref="ITimeQueryable{T}"/>.
    /// Public API uses DateTime; internal comparisons use DateTimeOffset (UTC).
    /// </summary>
    /// <typeparam name="T">Type of items stored in the set; must be non-nullable.</typeparam>
    public class TemporalSet<T> : ITimeQueryable<T> where T : notnull
    {
        private readonly ConcurrentDictionary<T, TemporalItem<T>> _dict = new();

        // Centralized policy for DateTimeKind.Unspecified handling.
        private const UnspecifiedPolicy DefaultPolicy = UnspecifiedPolicy.AssumeUtc;

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
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to)
        {
            var (fromUtc, toUtc) = TimeNormalization.NormalizeRange(from, to, unspecifiedPolicy: DefaultPolicy);
            long f = fromUtc.UtcTicks, t = toUtc.UtcTicks;

            return _dict.Values
                .Where(i => f <= i.Timestamp.UtcTicks && i.Timestamp.UtcTicks <= t)
                .OrderBy(i => i.Timestamp.UtcTicks)
                .ToList();
        }

        /// <summary>
        /// Removes all items whose timestamps are older than the specified cutoff date (strictly less).
        /// </summary>
        public void RemoveOlderThan(DateTime cutoff)
        {
            long c = TimeNormalization.UtcTicks(cutoff, DefaultPolicy);

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
        public TimeSpan GetTimeSpan()
        {
            if (_dict.IsEmpty) return TimeSpan.Zero;

            var snapshot = _dict.Values.ToList();
            if (snapshot.Count < 2) return TimeSpan.Zero;

            var min = snapshot[0].Timestamp;
            var max = snapshot[0].Timestamp;

            for (int i = 1; i < snapshot.Count; i++)
            {
                var ts = snapshot[i].Timestamp;
                if (ts < min) min = ts;
                if (ts > max) max = ts;
            }

            var span = max - min; // DateTimeOffset subtraction → TimeSpan
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }

        /// <summary>
        /// Counts the number of items whose timestamps fall within the specified inclusive time range.
        /// </summary>
        public int CountInRange(DateTime from, DateTime to)
        {
            var (fromUtc, toUtc) = TimeNormalization.NormalizeRange(from, to, unspecifiedPolicy: DefaultPolicy);
            long f = fromUtc.UtcTicks, t = toUtc.UtcTicks;

            int count = 0;
            foreach (var ti in _dict.Values)
            {
                long x = ti.Timestamp.UtcTicks;
                if (f <= x && x <= t) count++;
            }
            return count;
        }

        /// <summary>Removes all items from the set.</summary>
        public void Clear() => _dict.Clear();

        /// <summary>
        /// Removes all items whose timestamps fall within the specified inclusive time range.
        /// </summary>
        public void RemoveRange(DateTime from, DateTime to)
        {
            var (fromUtc, toUtc) = TimeNormalization.NormalizeRange(from, to, unspecifiedPolicy: DefaultPolicy);
            long f = fromUtc.UtcTicks, t = toUtc.UtcTicks;

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
        public TemporalItem<T>? GetLatest()
        {
            if (_dict.IsEmpty) return null;
            // MaxBy on ticks to be explicit and consistent
            return _dict.Values.MaxBy(ti => ti.Timestamp.UtcTicks);
        }

        /// <summary>
        /// Gets the temporal item with the earliest timestamp, or null if empty.
        /// </summary>
        public TemporalItem<T>? GetEarliest()
        {
            if (_dict.IsEmpty) return null;
            return _dict.Values.MinBy(ti => ti.Timestamp.UtcTicks);
        }

        /// <summary>
        /// Gets all items whose timestamps are strictly earlier than the specified time.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetBefore(DateTime time)
        {
            long cutoff = TimeNormalization.UtcTicks(time, DefaultPolicy);

            return _dict.Values
                .Where(i => i.Timestamp.UtcTicks < cutoff)
                .OrderBy(i => i.Timestamp.UtcTicks)
                .ToList();
        }

        /// <summary>
        /// Gets all items whose timestamps are strictly later than the specified time.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetAfter(DateTime time)
        {
            long cutoff = TimeNormalization.UtcTicks(time, DefaultPolicy);

            return _dict.Values
                .Where(i => i.Timestamp.UtcTicks > cutoff)
                .OrderBy(i => i.Timestamp.UtcTicks)
                .ToList();
        }

        /// <summary>
        /// Returns the number of items currently in the set.
        /// </summary>
        public int Count => _dict.Count;
    }
}