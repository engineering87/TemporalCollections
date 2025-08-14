// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using System.Collections.Concurrent;
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe temporal set where each unique item has an associated timestamp of insertion.
    /// Supports time-based queries and removal of old items via <see cref="ITimeQueryable{T}"/>.
    /// </summary>
    /// <typeparam name="T">Type of items stored in the set; must be non-nullable.</typeparam>
    public class TemporalSet<T> : ITimeQueryable<T> where T : notnull
    {
        private readonly ConcurrentDictionary<T, TemporalItem<T>> _dict = new();

        /// <summary>
        /// Adds an item to the set with the current timestamp if not already present.
        /// </summary>
        /// <param name="item">The item to add.</param>
        /// <returns>True if the item was added; false if it was already present.</returns>
        public bool Add(T item)
        {
            var temporalItem = TemporalItem<T>.Create(item);
            return _dict.TryAdd(item, temporalItem);
        }

        /// <summary>
        /// Checks whether the set contains the specified item.
        /// </summary>
        /// <param name="item">The item to check for containment.</param>
        /// <returns>True if the item is present; otherwise false.</returns>
        public bool Contains(T item) => _dict.ContainsKey(item);

        /// <summary>
        /// Removes the specified item from the set.
        /// </summary>
        /// <param name="item">The item to remove.</param>
        /// <returns>True if the item was removed; false if the item was not found.</returns>
        public bool Remove(T item)
        {
            return _dict.TryRemove(item, out _);
        }

        /// <summary>
        /// Returns all temporal items whose timestamps fall within the specified inclusive range.
        /// </summary>
        /// <param name="from">Start of the time range (inclusive).</param>
        /// <param name="to">End of the time range (inclusive).</param>
        /// <returns>An enumerable of <see cref="TemporalItem{T}"/> in the specified time range.</returns>
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to)
        {
            return _dict.Values
                .Where(i => i.Timestamp >= from && i.Timestamp <= to)
                .OrderBy(i => i.Timestamp)
                .ToList();
        }

        /// <summary>
        /// Removes all items whose timestamps are older than the specified cutoff date.
        /// </summary>
        /// <param name="cutoff">The cutoff timestamp; items older than this will be removed.</param>
        public void RemoveOlderThan(DateTime cutoff)
        {
            foreach (var kv in _dict)
            {
                if (kv.Value.Timestamp < cutoff)
                {
                    _dict.TryRemove(kv.Key, out _);
                }
            }
        }

        /// <summary>
        /// Gets a snapshot of all temporal items currently in the set.
        /// </summary>
        /// <returns>An enumerable of all <see cref="TemporalItem{T}"/> in the set.</returns>
        public IEnumerable<TemporalItem<T>> GetItems()
        {
            return _dict.Values
                .OrderBy(i => i.Timestamp)
                .ToList();
        }

        /// <summary>
        /// Calculates the time span between the earliest and latest items in the set.
        /// </summary>
        /// <returns>
        /// The <see cref="TimeSpan"/> between the earliest and latest timestamps,
        /// or <see cref="TimeSpan.Zero"/> if the set is empty.
        /// </returns>
        public TimeSpan GetTimeSpan()
        {
            if (_dict.IsEmpty)
                return TimeSpan.Zero;

            var snapshot = _dict.Values.ToList();
            var min = snapshot.Min(ti => ti.Timestamp);
            var max = snapshot.Max(ti => ti.Timestamp);
            return max - min;
        }

        /// <summary>
        /// Counts the number of items whose timestamps fall within the specified inclusive time range.
        /// </summary>
        /// <param name="from">The start of the time range (inclusive).</param>
        /// <param name="to">The end of the time range (inclusive).</param>
        /// <returns>The number of matching items.</returns>
        public int CountInRange(DateTime from, DateTime to)
        {
            return _dict.Values.Count(ti => ti.Timestamp >= from && ti.Timestamp <= to);
        }

        /// <summary>
        /// Removes all items from the set.
        /// </summary>
        public void Clear()
        {
            _dict.Clear();
        }

        /// <summary>
        /// Removes all items whose timestamps fall within the specified inclusive time range.
        /// </summary>
        /// <param name="from">The start of the time range (inclusive).</param>
        /// <param name="to">The end of the time range (inclusive).</param>
        public void RemoveRange(DateTime from, DateTime to)
        {
            foreach (var kv in _dict)
            {
                if (kv.Value.Timestamp >= from && kv.Value.Timestamp <= to)
                {
                    _dict.TryRemove(kv.Key, out _);
                }
            }
        }

        /// <summary>
        /// Gets the temporal item with the latest timestamp.
        /// </summary>
        /// <returns>The most recent <see cref="TemporalItem{T}"/>, or null if the set is empty.</returns>
        public TemporalItem<T>? GetLatest()
        {
            if (_dict.IsEmpty)
                return null;

            return _dict.Values.MaxBy(ti => ti.Timestamp);
        }

        /// <summary>
        /// Gets the temporal item with the earliest timestamp.
        /// </summary>
        /// <returns>The earliest <see cref="TemporalItem{T}"/>, or null if the set is empty.</returns>
        public TemporalItem<T>? GetEarliest()
        {
            if (_dict.IsEmpty)
                return null;

            return _dict.Values.MinBy(ti => ti.Timestamp);
        }

        /// <summary>
        /// Gets all items whose timestamps are strictly earlier than the specified time.
        /// </summary>
        /// <param name="time">The cutoff time; returned items will have timestamps less than this value.</param>
        /// <returns>An enumerable of matching items.</returns>
        public IEnumerable<TemporalItem<T>> GetBefore(DateTime time)
        {
            return _dict.Values
                .Where(i => i.Timestamp < time)
                .OrderBy(i => i.Timestamp)
                .ToList();
        }

        /// <summary>
        /// Gets all items whose timestamps are strictly later than the specified time.
        /// </summary>
        /// <param name="time">The cutoff time; returned items will have timestamps greater than this value.</param>
        /// <returns>An enumerable of matching items.</returns>
        public IEnumerable<TemporalItem<T>> GetAfter(DateTime time)
        {
            return _dict.Values
                .Where(i => i.Timestamp > time)
                .OrderBy(i => i.Timestamp)
                .ToList();
        }

        /// <summary>
        /// Returns the number of items currently in the set.
        /// </summary>
        public int Count => _dict.Count;
    }
}