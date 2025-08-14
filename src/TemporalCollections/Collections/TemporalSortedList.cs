// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// Thread-safe sorted list of <see cref="TemporalItem{T}"/>, ordered by timestamp ascending.
    /// Implements <see cref="ITimeQueryable{T}"/> for time-based querying and cleanup.
    /// </summary>
    /// <typeparam name="T">The type of items stored in the list.</typeparam>
    public class TemporalSortedList<T> : ITimeQueryable<T>
    {
        private readonly List<TemporalItem<T>> _items = [];
        private readonly Lock _lock = new();

        /// <summary>
        /// Adds a new item to the list while preserving chronological order.
        /// </summary>
        /// <param name="item">The value to wrap in a <see cref="TemporalItem{T}"/> and store.</param>
        public void Add(T item)
        {
            var temporalItem = TemporalItem<T>.Create(item);
            lock (_lock)
            {
                // Binary search to find insertion index
                int index = _items.BinarySearch(temporalItem, TemporalItem<T>.TimestampComparer);
                if (index < 0) index = ~index;
                _items.Insert(index, temporalItem);
            }
        }

        /// <summary>
        /// Retrieves all temporal items in the sorted list whose timestamps
        /// are within the inclusive range from <paramref name="from"/> to <paramref name="to"/>.
        /// The list is locked during the operation for thread safety.
        /// </summary>
        /// <param name="from">The start of the timestamp range (inclusive).</param>
        /// <param name="to">The end of the timestamp range (inclusive).</param>
        /// <returns>
        /// A list of <see cref="TemporalItem{T}"/> instances ordered by timestamp,
        /// whose timestamps fall within the specified range.
        /// </returns>
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("to must be >= from", nameof(to));

            lock (_lock)
            {
                if (_items.Count == 0) return Array.Empty<TemporalItem<T>>();

                int start = FindFirstIndexAtOrAfter(from);
                if (start >= _items.Count) return Array.Empty<TemporalItem<T>>();

                int end = FindLastIndexAtOrBefore(to);
                if (end < start) return Array.Empty<TemporalItem<T>>();

                int count = end - start + 1;
                return _items.GetRange(start, count);
            }
        }

        /// <summary>
        /// Removes all temporal items from the sorted list whose timestamps
        /// are earlier than the specified <paramref name="cutoff"/> timestamp.
        /// The operation is thread-safe and locks the list during modification.
        /// </summary>
        /// <param name="cutoff">The cutoff timestamp; items with timestamps less than this are removed.</param>
        public void RemoveOlderThan(DateTime cutoff)
        {
            lock (_lock)
            {
                if (_items.Count == 0) return;
                int index = FindFirstIndexAtOrAfter(cutoff);
                if (index > 0)
                    _items.RemoveRange(0, index);
            }
        }

        /// <summary>
        /// Returns the number of items currently stored in the list.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _items.Count;
                }
            }
        }

        /// <summary>
        /// Finds the index of the first element with a timestamp greater than or equal to <paramref name="target"/>.
        /// Returns <see cref="Count"/> if no such element exists.
        /// </summary>
        /// <param name="target">The timestamp to search for.</param>
        /// <returns>The index of the first matching element, or <see cref="Count"/> if none found.</returns>
        private int FindFirstIndexAtOrAfter(DateTime target)
        {
            int left = 0;
            int right = _items.Count - 1;
            int result = _items.Count;

            while (left <= right)
            {
                int mid = (left + right) / 2;
                if (_items[mid].Timestamp >= target)
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
        /// Returns the total timespan covered by items in the collection,
        /// computed as (latest.Timestamp - earliest.Timestamp). Returns
        /// <see cref="TimeSpan.Zero"/> if the list is empty or contains a single item.
        /// </summary>
        public TimeSpan GetTimeSpan()
        {
            lock (_lock)
            {
                if (_items.Count < 2) return TimeSpan.Zero;
                var span = _items[^1].Timestamp - _items[0].Timestamp;
                return span < TimeSpan.Zero ? TimeSpan.Zero : span;
            }
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

            lock (_lock)
            {
                if (_items.Count == 0) return 0;

                int start = FindFirstIndexAtOrAfter(from);
                if (start >= _items.Count) return 0;

                int end = FindLastIndexAtOrBefore(to);
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
            {
                _items.Clear();
            }
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

            lock (_lock)
            {
                if (_items.Count == 0) return;

                int start = FindFirstIndexAtOrAfter(from);
                if (start >= _items.Count) return;

                int end = FindLastIndexAtOrBefore(to);
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
        /// <param name="time">Exclusive upper bound for the timestamp.</param>
        public IEnumerable<TemporalItem<T>> GetBefore(DateTime time)
        {
            lock (_lock)
            {
                int idx = FindFirstIndexAtOrAfter(time); // first >= time
                if (idx <= 0) return [];
                return _items.GetRange(0, idx);
            }
        }

        /// <summary>
        /// Retrieves all items with timestamp strictly after <paramref name="time"/>.
        /// </summary>
        /// <param name="time">Exclusive lower bound for the timestamp.</param>
        public IEnumerable<TemporalItem<T>> GetAfter(DateTime time)
        {
            lock (_lock)
            {
                int idx = FindFirstIndexAfter(time); // first > time
                if (idx >= _items.Count) return [];
                return _items.GetRange(idx, _items.Count - idx);
            }
        }

        #region Internal helpers

        /// <summary>
        /// Finds the index of the first element with a timestamp strictly greater than <paramref name="target"/>.
        /// Returns <see cref="Count"/> if no such element exists.
        /// </summary>
        private int FindFirstIndexAfter(DateTime target)
        {
            int left = 0;
            int right = _items.Count - 1;
            int result = _items.Count;

            while (left <= right)
            {
                int mid = (left + right) / 2;
                if (_items[mid].Timestamp > target)
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
        /// Finds the index of the last element with a timestamp less than or equal to <paramref name="target"/>.
        /// Returns -1 if all elements are greater than <paramref name="target"/>.
        /// </summary>
        private int FindLastIndexAtOrBefore(DateTime target)
        {
            int left = 0;
            int right = _items.Count - 1;
            int result = -1;

            while (left <= right)
            {
                int mid = (left + right) / 2;
                if (_items[mid].Timestamp <= target)
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