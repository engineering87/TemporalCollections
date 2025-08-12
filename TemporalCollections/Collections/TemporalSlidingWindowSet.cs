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
        /// <returns>An enumeration of all temporal items.</returns>
        public IEnumerable<TemporalItem<T>> GetItems()
        {
            return _dict.Values.ToList();
        }

        /// <inheritdoc />
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to)
        {
            return _dict.Values
                .Where(i => i.Timestamp >= from && i.Timestamp <= to)
                .ToList();
        }

        /// <inheritdoc />
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
        /// Gets the current number of items in the set.
        /// </summary>
        public int Count => _dict.Count;
    }
}