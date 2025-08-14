// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using System.Collections.Concurrent;
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe set of temporal items where each item is retained only within a sliding time window.
    /// Items older than the configured window size are considered expired and can be removed.
    /// Implements <see cref="ITimeQueryable{T}"/> for time-based queries and cleanup.
    /// </summary>
    /// <typeparam name="T">Type of items stored in the set; must be non-nullable.</typeparam>
    public class TemporalSlidingWindowSet<T> : ITimeQueryable<T> where T : notnull
    {
        private readonly TimeSpan _windowSize;
        private readonly ConcurrentDictionary<T, TemporalItem<T>> _dict = new();

        /// <summary>
        /// Initializes a new instance of the <see cref="TemporalSlidingWindowSet{T}"/> class with the specified window size.
        /// </summary>
        /// <param name="windowSize">The sliding time window duration during which items are considered valid.</param>
        /// <exception cref="ArgumentException">Thrown if <paramref name="windowSize"/> is zero or negative.</exception>
        public TemporalSlidingWindowSet(TimeSpan windowSize)
        {
            if (windowSize <= TimeSpan.Zero)
                throw new ArgumentException("Window size must be positive.", nameof(windowSize));

            _windowSize = windowSize;
        }

        /// <summary>
        /// Attempts to add an item to the set with the current timestamp.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <returns>True if the item was added; false if it was already present.</returns>
        public bool Add(T item)
        {
            var temporalItem = TemporalItem<T>.Create(item);
            return _dict.TryAdd(item, temporalItem);
        }

        /// <summary>
        /// Removes all items from the set that have expired based on the sliding window size.
        /// </summary>
        public void RemoveExpired()
        {
            var cutoff = DateTime.UtcNow - _windowSize;

            foreach (var kvp in _dict)
            {
                if (kvp.Value.Timestamp < cutoff)
                {
                    _dict.TryRemove(kvp.Key, out _);
                }
            }
        }

        /// <summary>
        /// Returns a snapshot of all temporal items currently in the set.
        /// </summary>
        /// <returns>An enumeration of all temporal items ordered ascending by timestamp</returns>
        public IEnumerable<TemporalItem<T>> GetItems()
        {
            // Return a stable, ordered snapshot (ascending by timestamp)
            return _dict.Values
                .OrderBy(i => i.Timestamp)
                .ToList();
        }

        /// <summary>
        /// Retrieves all temporal items in the collection whose timestamps
        /// fall within the inclusive range from <paramref name="from"/> to <paramref name="to"/>.
        /// </summary>
        /// <param name="from">The start of the timestamp range (inclusive).</param>
        /// <param name="to">The end of the timestamp range (inclusive).</param>
        /// <returns>
        /// A collection of <see cref="TemporalItem{T}"/> instances whose timestamps
        /// are within the specified range.
        /// </returns>
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("to must be >= from", nameof(to));

            return _dict.Values
                .Where(i => i.Timestamp >= from && i.Timestamp <= to)
                .OrderBy(i => i.Timestamp)
                .ToList();
        }        

        /// <summary>
        /// Removes all items from the collection that have timestamps older than the specified cutoff.
        /// </summary>
        /// <param name="cutoff">The cutoff timestamp; items with timestamps less than this will be removed.</param>
        public void RemoveOlderThan(DateTime cutoff)
        {
            foreach (var kvp in _dict)
            {
                if (kvp.Value.Timestamp < cutoff)
                {
                    _dict.TryRemove(kvp.Key, out _);
                }
            }
        }

        /// <summary>
        /// Returns the total timespan covered by items in the collection,
        /// computed as (latest.Timestamp - earliest.Timestamp).
        /// Returns <see cref="TimeSpan.Zero"/> if there are fewer than two items.
        /// </summary>
        public TimeSpan GetTimeSpan()
        {
            // Single pass to get min/max timestamps.
            bool any = false;
            DateTime min = DateTime.MaxValue;
            DateTime max = DateTime.MinValue;

            foreach (var item in _dict.Values)
            {
                any = true;
                var ts = item.Timestamp;
                if (ts < min) min = ts;
                if (ts > max) max = ts;
            }

            if (!any || min == DateTime.MaxValue || max == DateTime.MinValue || min >= max)
                return TimeSpan.Zero;

            var span = max - min;
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }

        /// <summary>
        /// Returns the number of items with timestamps in the inclusive range [from, to].
        /// </summary>
        /// <param name="from">Range start (inclusive).</param>
        /// <param name="to">Range end (inclusive).</param>
        public int CountInRange(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("to must be >= from", nameof(to));

            int count = 0;
            foreach (var item in _dict.Values)
            {
                var ts = item.Timestamp;
                if (ts >= from && ts <= to)
                    count++;
            }
            return count;
        }

        /// <summary>
        /// Removes all items from the collection.
        /// </summary>
        public void Clear()
        {
            _dict.Clear();
        }

        /// <summary>
        /// Removes all items whose timestamps fall within the inclusive range [from, to].
        /// </summary>
        /// <param name="from">Range start (inclusive).</param>
        /// <param name="to">Range end (inclusive).</param>
        public void RemoveRange(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("to must be >= from", nameof(to));

            foreach (var kvp in _dict)
            {
                var ts = kvp.Value.Timestamp;
                if (ts >= from && ts <= to)
                    _dict.TryRemove(kvp.Key, out _);
            }
        }

        /// <summary>
        /// Retrieves the latest (most recent) item by timestamp, or null if the set is empty.
        /// </summary>
        public TemporalItem<T>? GetLatest()
        {
            TemporalItem<T>? best = null;
            DateTime bestTs = DateTime.MinValue;

            foreach (var item in _dict.Values)
            {
                if (item.Timestamp > bestTs)
                {
                    bestTs = item.Timestamp;
                    best = item;
                }
            }

            return best;
        }

        /// <summary>
        /// Retrieves the earliest (oldest) item by timestamp, or null if the set is empty.
        /// </summary>
        public TemporalItem<T>? GetEarliest()
        {
            TemporalItem<T>? best = null;
            DateTime bestTs = DateTime.MaxValue;

            foreach (var item in _dict.Values)
            {
                if (item.Timestamp < bestTs)
                {
                    bestTs = item.Timestamp;
                    best = item;
                }
            }

            return best;
        }

        /// <summary>
        /// Retrieves all items with timestamp strictly before <paramref name="time"/>.
        /// </summary>
        /// <param name="time">Exclusive upper bound for the timestamp.</param>
        public IEnumerable<TemporalItem<T>> GetBefore(DateTime time)
        {
            // Strictly before; return ordered snapshot
            var result = new List<TemporalItem<T>>();
            foreach (var item in _dict.Values)
                if (item.Timestamp < time) result.Add(item);

            return result
                .OrderBy(i => i.Timestamp)
                .ToList();
        }

        /// <summary>
        /// Retrieves all items with timestamp strictly after <paramref name="time"/>.
        /// </summary>
        /// <param name="time">Exclusive lower bound for the timestamp.</param>
        public IEnumerable<TemporalItem<T>> GetAfter(DateTime time)
        {
            // Strictly after; return ordered snapshot
            var result = new List<TemporalItem<T>>();
            foreach (var item in _dict.Values)
                if (item.Timestamp > time) result.Add(item);

            return result
                .OrderBy(i => i.Timestamp)
                .ToList();
        }

        /// <summary>
        /// Gets the current number of items in the set.
        /// </summary>
        public int Count => _dict.Count;
    }
}