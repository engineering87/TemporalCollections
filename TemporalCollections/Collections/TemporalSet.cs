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
                        .Where(ti => ti.Timestamp >= from && ti.Timestamp <= to)
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
            return _dict.Values.ToList();
        }

        /// <summary>
        /// Returns the number of items currently in the set.
        /// </summary>
        public int Count => _dict.Count;
    }
}