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
            lock (_lock)
            {
                int startIndex = FindFirstIndexAtOrAfter(from);
                var results = new List<TemporalItem<T>>();

                for (int i = startIndex; i < _items.Count; i++)
                {
                    if (_items[i].Timestamp > to) break;
                    results.Add(_items[i]);
                }

                return results;
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
                int index = FindFirstIndexAtOrAfter(cutoff);
                if (index > 0)
                {
                    _items.RemoveRange(0, index);
                }
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
    }
}