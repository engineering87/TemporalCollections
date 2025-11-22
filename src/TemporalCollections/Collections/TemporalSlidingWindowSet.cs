// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using System.Collections.Concurrent;
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// Thread-safe sorted list of <see cref="TemporalItem{T}"/>, ordered by timestamp ascending.
    /// Implements <see cref="ITimeQueryable{T}"/> for time-based querying and cleanup.
    /// Public API uses DateTime; internal comparisons are done with DateTimeOffset (UTC) internally.
    /// </summary>
    /// <typeparam name="T">The type of items stored in the list.</typeparam>
    public class TemporalSlidingWindowSet<T> : TimeQueryableBase<T> where T : notnull
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
        public bool Add(T item)
        {
            var temporalItem = TemporalItem<T>.Create(item); // Timestamp is DateTimeOffset UTC (monotonic)
            return _dict.TryAdd(item, temporalItem);
        }

        /// <summary>
        /// Removes all items from the set that have expired based on the sliding window size.
        /// </summary>
        public void RemoveExpired()
        {
            var cutoff = DateTimeOffset.UtcNow - _windowSize;
            long cutoffTicks = cutoff.UtcTicks;

            foreach (var kvp in _dict)
            {
                if (kvp.Value.Timestamp.UtcTicks < cutoffTicks)
                {
                    _dict.TryRemove(kvp.Key, out _);
                }
            }
        }

        /// <summary>
        /// Returns a snapshot of all temporal items currently in the set, ordered by ascending timestamp.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetItems()
        {
            return _dict.Values
                .OrderBy(i => i.Timestamp.UtcTicks)
                .ToList();
        }

        /// <summary>
        /// Retrieves all temporal items in the inclusive range [from, to], ordered by timestamp.
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
        /// Removes all items with Timestamp &lt; cutoff (exclusive).
        /// </summary>
        public override void RemoveOlderThan(DateTimeOffset cutoff)
        {
            long c = cutoff.UtcTicks;

            foreach (var kvp in _dict)
            {
                if (kvp.Value.Timestamp.UtcTicks < c)
                {
                    _dict.TryRemove(kvp.Key, out _);
                }
            }
        }

        /// <summary>
        /// Returns the total timespan covered by items,
        /// computed as (latest.Timestamp - earliest.Timestamp). Returns TimeSpan.Zero if &lt; 2 items.
        /// </summary>
        public override TimeSpan GetTimeSpan()
        {
            bool any = false;
            DateTimeOffset min = DateTimeOffset.MaxValue;
            DateTimeOffset max = DateTimeOffset.MinValue;

            foreach (var item in _dict.Values)
            {
                any = true;
                var ts = item.Timestamp;
                if (ts < min) min = ts;
                if (ts > max) max = ts;
            }

            if (!any || min >= max) return TimeSpan.Zero;
            var span = max - min; // DateTimeOffset subtraction -> TimeSpan
            return span < TimeSpan.Zero ? TimeSpan.Zero : span;
        }

        /// <summary>
        /// Returns the number of items with timestamps in the inclusive range [from, to].
        /// </summary>
        public override int CountInRange(DateTimeOffset from, DateTimeOffset to)
        {
            long f = from.UtcTicks, t = to.UtcTicks;
            if (f > t)
                (f, t) = (t, f);

            int count = 0;
            foreach (var item in _dict.Values)
            {
                long x = item.Timestamp.UtcTicks;
                if (f <= x && x <= t) count++;
            }
            return count;
        }

        /// <summary>
        /// Removes all items from the collection.
        /// </summary>
        public override void Clear() => _dict.Clear();

        /// <summary>
        /// Removes all items whose timestamps fall within the inclusive range [from, to].
        /// </summary>
        public override void RemoveRange(DateTimeOffset from, DateTimeOffset to)
        {
            long f = from.UtcTicks, t = to.UtcTicks;
            if (f > t) 
                (f, t) = (t, f);

            foreach (var kvp in _dict)
            {
                long x = kvp.Value.Timestamp.UtcTicks;
                if (f <= x && x <= t)
                    _dict.TryRemove(kvp.Key, out _);
            }
        }

        /// <summary>
        /// Retrieves the latest (most recent) item by timestamp, or null if the set is empty.
        /// </summary>
        public override TemporalItem<T>? GetLatest()
        {
            TemporalItem<T>? best = null;
            long bestTicks = long.MinValue;

            foreach (var item in _dict.Values)
            {
                long x = item.Timestamp.UtcTicks;
                if (x > bestTicks) { bestTicks = x; best = item; }
            }
            return best;
        }

        /// <summary>
        /// Retrieves the earliest (oldest) item by timestamp, or null if the set is empty.
        /// </summary>
        public override TemporalItem<T>? GetEarliest()
        {
            TemporalItem<T>? best = null;
            long bestTicks = long.MaxValue;

            foreach (var item in _dict.Values)
            {
                long x = item.Timestamp.UtcTicks;
                if (x < bestTicks) { bestTicks = x; best = item; }
            }
            return best;
        }

        /// <summary>
        /// Retrieves all items with timestamp strictly before <paramref name="time"/>, ordered by ascending timestamp.
        /// </summary>
        public override IEnumerable<TemporalItem<T>> GetBefore(DateTimeOffset time)
        {
            long cutoff = time.UtcTicks;

            var result = new List<TemporalItem<T>>();
            foreach (var item in _dict.Values)
                if (item.Timestamp.UtcTicks < cutoff) result.Add(item);

            return result
                .OrderBy(i => i.Timestamp.UtcTicks)
                .ToList();
        }

        /// <summary>
        /// Retrieves all items with timestamp strictly after <paramref name="time"/>, ordered by ascending timestamp.
        /// </summary>
        public override IEnumerable<TemporalItem<T>> GetAfter(DateTimeOffset time)
        {
            long cutoff = time.UtcTicks;

            var result = new List<TemporalItem<T>>();
            foreach (var item in _dict.Values)
                if (item.Timestamp.UtcTicks > cutoff) result.Add(item);

            return result
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
                    // Tie-break: prefer the later item (>= time).
                    bool itemIsAfterOrEqual = ticks >= target;
                    bool bestIsAfterOrEqual = best.Timestamp.UtcTicks >= target;

                    if (itemIsAfterOrEqual && !bestIsAfterOrEqual)
                    {
                        best = item;
                    }
                    else if (itemIsAfterOrEqual == bestIsAfterOrEqual)
                    {
                        // If both on the same side of target, pick the later one for determinism.
                        if (ticks > best.Timestamp.UtcTicks)
                            best = item;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Gets the current number of items in the set.
        /// </summary>
        public int Count => _dict.Count;
    }
}