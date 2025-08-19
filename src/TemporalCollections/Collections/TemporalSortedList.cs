// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;
using TemporalCollections.Utilities;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// Thread-safe sorted list of <see cref="TemporalItem{T}"/>, ordered by timestamp ascending.
    /// Implements <see cref="ITimeQueryable{T}"/> for time-based querying and cleanup.
    /// Public method signatures use DateTime, but all comparisons are done on DateTimeOffset (UTC) internally.
    /// </summary>
    /// <typeparam name="T">The type of items stored in the list.</typeparam>
    public class TemporalSortedList<T> : ITimeQueryable<T>
    {
        private readonly List<TemporalItem<T>> _items = [];
        private readonly Lock _lock = new();

        // Centralize how Unspecified DateTimes are handled.
        private const UnspecifiedPolicy DefaultPolicy = UnspecifiedPolicy.AssumeUtc;

        /// <summary>
        /// Adds a new item to the list while preserving chronological order.
        /// </summary>
        public void Add(T item)
        {
            var temporalItem = TemporalItem<T>.Create(item);
            lock (_lock)
            {
                // Binary search to find insertion index using the item's comparer (by DateTimeOffset)
                int index = _items.BinarySearch(temporalItem, TemporalItem<T>.TimestampComparer);
                if (index < 0) index = ~index;
                _items.Insert(index, temporalItem);
            }
        }

        /// <summary>
        /// Retrieves all temporal items in the inclusive range [from, to], ordered by timestamp.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to)
        {
            var (fromUtc, toUtc) = TimeNormalization.NormalizeRange(from, to, unspecifiedPolicy: DefaultPolicy);
            long f = fromUtc.UtcTicks, t = toUtc.UtcTicks;

            lock (_lock)
            {
                if (_items.Count == 0) return [];

                int start = FindFirstIndexAtOrAfterUtcTicks(f);
                if (start >= _items.Count) return [];

                int end = FindLastIndexAtOrBeforeUtcTicks(t);
                if (end < start) return [];

                int count = end - start + 1;
                return _items.GetRange(start, count);
            }
        }

        /// <summary>
        /// Removes all items with Timestamp &lt; cutoff (exclusive).
        /// </summary>
        public void RemoveOlderThan(DateTime cutoff)
        {
            long c = TimeNormalization.UtcTicks(cutoff, DefaultPolicy);

            lock (_lock)
            {
                if (_items.Count == 0) return;

                // first index with ts >= cutoff  -> remove [0, idx)
                int idx = FindFirstIndexAtOrAfterUtcTicks(c);
                if (idx > 0) _items.RemoveRange(0, idx);
            }
        }

        /// <summary>
        /// Returns the number of items currently stored in the list.
        /// </summary>
        public int Count
        {
            get 
            { 
                lock (_lock) return _items.Count; 
            }
        }

        /// <summary>
        /// Returns the total timespan covered by items, or TimeSpan.Zero if fewer than two.
        /// </summary>
        public TimeSpan GetTimeSpan()
        {
            lock (_lock)
            {
                if (_items.Count < 2) return TimeSpan.Zero;
                var span = _items[^1].Timestamp - _items[0].Timestamp; // DateTimeOffset subtraction → TimeSpan
                return span < TimeSpan.Zero ? TimeSpan.Zero : span;
            }
        }

        /// <summary>
        /// Returns the number of items with timestamps in the inclusive range [from, to].
        /// </summary>
        public int CountInRange(DateTime from, DateTime to)
        {
            var (fromUtc, toUtc) = TimeNormalization.NormalizeRange(from, to, unspecifiedPolicy: DefaultPolicy);
            long f = fromUtc.UtcTicks, t = toUtc.UtcTicks;

            lock (_lock)
            {
                if (_items.Count == 0) return 0;

                int start = FindFirstIndexAtOrAfterUtcTicks(f);
                if (start >= _items.Count) return 0;

                int end = FindLastIndexAtOrBeforeUtcTicks(t);
                if (end < start) return 0;

                return end - start + 1;
            }
        }

        /// <summary>
        /// Removes all items from the collection.
        /// </summary>
        public void Clear()
        {
            lock (_lock) 
                _items.Clear();
        }

        /// <summary>
        /// Removes all items whose timestamps fall within the inclusive range [from, to].
        /// </summary>
        public void RemoveRange(DateTime from, DateTime to)
        {
            var (fromUtc, toUtc) = TimeNormalization.NormalizeRange(from, to, unspecifiedPolicy: DefaultPolicy);
            long f = fromUtc.UtcTicks, t = toUtc.UtcTicks;

            lock (_lock)
            {
                if (_items.Count == 0) return;

                int start = FindFirstIndexAtOrAfterUtcTicks(f);
                if (start >= _items.Count) return;

                int end = FindLastIndexAtOrBeforeUtcTicks(t);
                if (end < start) return;

                int count = end - start + 1;
                _items.RemoveRange(start, count);
            }
        }

        /// <summary>
        /// Retrieves the latest item by timestamp, or null if the collection is empty.
        /// </summary>
        public TemporalItem<T>? GetLatest()
        {
            lock (_lock)
            {
                if (_items.Count == 0) return null;
                return _items[^1];
            }
        }

        /// <summary>
        /// Retrieves the earliest item by timestamp, or null if the collection is empty.
        /// </summary>
        public TemporalItem<T>? GetEarliest()
        {
            lock (_lock)
            {
                if (_items.Count == 0) return null;
                return _items[0];
            }
        }

        /// <summary>
        /// Retrieves all items with timestamp strictly before <paramref name="time"/>.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetBefore(DateTime time)
        {
            long cutoff = TimeNormalization.UtcTicks(time, DefaultPolicy);

            lock (_lock)
            {
                // first index with ts >= cutoff  → take [0, idx)
                int idx = FindFirstIndexAtOrAfterUtcTicks(cutoff);
                if (idx <= 0) return [];
                return _items.GetRange(0, idx);
            }
        }

        /// <summary>
        /// Retrieves all items with timestamp strictly after <paramref name="time"/>.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetAfter(DateTime time)
        {
            long cutoff = TimeNormalization.UtcTicks(time, DefaultPolicy);

            lock (_lock)
            {
                // first index with ts > cutoff → take [idx, end)
                int idx = FindFirstIndexAfterUtcTicks(cutoff);
                if (idx >= _items.Count) return [];
                return _items.GetRange(idx, _items.Count - idx);
            }
        }

        /// <summary>
        /// Counts the number of items with timestamp greater than or equal to the specified cutoff.
        /// Runs in O(log n) using binary search on the sorted timestamps.
        /// </summary>
        public int CountSince(DateTime from)
        {
            long f = TimeNormalization.UtcTicks(from, DefaultPolicy);

            lock (_lock)
            {
                if (_items.Count == 0) return 0;

                int idx = FindFirstIndexAtOrAfterUtcTicks(f);
                if (idx >= _items.Count) return 0;

                return _items.Count - idx;
            }
        }

        /// <summary>
        /// Returns the item whose timestamp is closest to <paramref name="time"/>.
        /// If the list is empty, returns <c>null</c>.
        /// In case of a tie (same distance before/after), the later item (timestamp ≥ time) is returned.
        /// Complexity: O(log n).
        /// </summary>
        public TemporalItem<T>? GetNearest(DateTime time)
        {
            long target = TimeNormalization.UtcTicks(time, DefaultPolicy);

            lock (_lock)
            {
                int n = _items.Count;
                if (n == 0) return null;

                // First index with ts >= target (or n if all < target)
                int idx = FindFirstIndexAtOrAfterUtcTicks(target);

                if (idx == 0) return _items[0];
                if (idx == n) return _items[n - 1];

                long beforeDiff = target - _items[idx - 1].Timestamp.UtcTicks; // >= 0
                long afterDiff = _items[idx].Timestamp.UtcTicks - target;     // >= 0

                // Tie-break: prefer the later item (>= time)
                return (afterDiff <= beforeDiff) ? _items[idx] : _items[idx - 1];
            }
        }

        #region Internal helpers (binary searches on UtcTicks)

        /// <summary>
        /// Finds the index of the first element with timestamp.UtcTicks >= targetTicks.
        /// Returns Count if no such element exists.
        /// </summary>
        private int FindFirstIndexAtOrAfterUtcTicks(long targetTicks)
        {
            int left = 0;
            int right = _items.Count - 1;
            int result = _items.Count;

            while (left <= right)
            {
                int mid = (left + right) / 2;
                long midTicks = _items[mid].Timestamp.UtcTicks;

                if (midTicks >= targetTicks)
                {
                    result = mid;
                    right = mid - 1;
                }
                else
                {
                    left = mid + 1;
                }
            }
            return result;
        }

        /// <summary>
        /// Finds the index of the first element with timestamp.UtcTicks > targetTicks.
        /// Returns Count if no such element exists.
        /// </summary>
        private int FindFirstIndexAfterUtcTicks(long targetTicks)
        {
            int left = 0;
            int right = _items.Count - 1;
            int result = _items.Count;

            while (left <= right)
            {
                int mid = (left + right) / 2;
                long midTicks = _items[mid].Timestamp.UtcTicks;

                if (midTicks > targetTicks)
                {
                    result = mid;
                    right = mid - 1;
                }
                else
                {
                    left = mid + 1;
                }
            }
            return result;
        }

        /// <summary>
        /// Finds the index of the last element with timestamp.UtcTicks <= targetTicks.
        /// Returns -1 if all elements are greater than targetTicks.
        /// </summary>
        private int FindLastIndexAtOrBeforeUtcTicks(long targetTicks)
        {
            int left = 0;
            int right = _items.Count - 1;
            int result = -1;

            while (left <= right)
            {
                int mid = (left + right) / 2;
                long midTicks = _items[mid].Timestamp.UtcTicks;

                if (midTicks <= targetTicks)
                {
                    result = mid;
                    left = mid + 1;
                }
                else
                {
                    right = mid - 1;
                }
            }
            return result;
        }

        #endregion
    }
}