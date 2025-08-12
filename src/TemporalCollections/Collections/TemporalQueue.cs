// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using System.Collections.Concurrent;
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe FIFO queue where each item has an insertion timestamp,
    /// enabling efficient time-based queries and removal of old elements.
    /// </summary>
    /// <typeparam name="T">Type of items stored in the queue.</typeparam>
    public class TemporalQueue<T> : ITimeQueryable<T>
    {
        private readonly ConcurrentQueue<TemporalItem<T>> _queue = new();

        /// <summary>
        /// Gets the total number of items currently in the queue.
        /// </summary>
        public int Count => _queue.Count;

        /// <summary>
        /// Enqueues an item with the current timestamp.
        /// </summary>
        /// <param name="item">The item to enqueue.</param>
        public void Enqueue(T item)
        {
            var temporalItem = TemporalItem<T>.Create(item);
            _queue.Enqueue(temporalItem);
        }

        /// <summary>
        /// Dequeues the oldest item from the queue.
        /// </summary>
        /// <returns>The dequeued <see cref="TemporalItem{T}"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the queue is empty.</exception>
        public TemporalItem<T> Dequeue()
        {
            if (_queue.TryDequeue(out var item))
                return item;
            throw new InvalidOperationException("Queue is empty.");
        }

        /// <summary>
        /// Returns the item at the front of the queue without removing it.
        /// </summary>
        /// <returns>The front <see cref="TemporalItem{T}"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the queue is empty.</exception>
        public TemporalItem<T> Peek()
        {
            if (_queue.TryPeek(out var item))
                return item;
            throw new InvalidOperationException("Queue is empty.");
        }

        /// <summary>
        /// Retrieves all items whose timestamps are within the specified inclusive time range.
        /// </summary>
        /// <param name="from">Start of the time range (inclusive).</param>
        /// <param name="to">End of the time range (inclusive).</param>
        /// <returns>A list of matching items.</returns>
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to)
        {
            return _queue
                .Where(i => i.Timestamp >= from && i.Timestamp <= to)
                .ToList();
        }

        /// <summary>
        /// Removes items from the front of the queue until all remaining items
        /// have timestamps equal to or newer than the cutoff.
        /// </summary>
        /// <param name="cutoff">The cutoff timestamp; older items will be removed.</param>
        public void RemoveOlderThan(DateTime cutoff)
        {
            while (_queue.TryPeek(out var item) && item.Timestamp < cutoff)
            {
                _queue.TryDequeue(out _);
            }
        }
    }
}