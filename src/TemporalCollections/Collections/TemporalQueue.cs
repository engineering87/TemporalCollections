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
        private readonly Lock _lock = new();

        /// <summary>
        /// Gets the total number of items currently in the queue.
        /// Note: this is a snapshot and runs in O(n).
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock) { return _queue.Count; }
            }
        }

        /// <summary>
        /// Enqueues an item with the current timestamp.
        /// </summary>
        /// <param name="item">The item to enqueue.</param>
        public void Enqueue(T item)
        {
            var temporalItem = TemporalItem<T>.Create(item);
            lock (_lock)
            {
                _queue.Enqueue(temporalItem);
            }
        }

        /// <summary>
        /// Dequeues the oldest item from the queue.
        /// </summary>
        /// <returns>The dequeued <see cref="TemporalItem{T}"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the queue is empty.</exception>
        public TemporalItem<T> Dequeue()
        {
            lock (_lock)
            {
                if (_queue.TryDequeue(out var item))
                    return item;
                throw new InvalidOperationException("Queue is empty.");
            }
        }

        /// <summary>
        /// Returns the item at the front of the queue without removing it.
        /// </summary>
        /// <returns>The front <see cref="TemporalItem{T}"/>.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the queue is empty.</exception>
        public TemporalItem<T> Peek()
        {
            lock (_lock)
            {
                if (_queue.TryPeek(out var item))
                    return item;
                throw new InvalidOperationException("Queue is empty.");
            }
        }

        /// <summary>
        /// Retrieves all items whose timestamps are within the specified inclusive time range.
        /// The snapshot is taken under lock to ensure a consistent view.
        /// </summary>
        /// <param name="from">Start of the time range (inclusive).</param>
        /// <param name="to">End of the time range (inclusive).</param>
        /// <returns>A list of matching items.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="to"/> is earlier than <paramref name="from"/>.</exception>
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("to must be >= from", nameof(to));

            lock (_lock)
            {
                // ConcurrentQueue enumerates a moment-in-time snapshot; we still hold the lock
                // to keep semantics consistent across all public methods.
                return _queue.Where(i => i.Timestamp >= from && i.Timestamp <= to)
                             .ToList();
            }
        }

        /// <summary>
        /// Removes items from the front of the queue until all remaining items
        /// have timestamps equal to or newer than the cutoff.
        /// </summary>
        /// <param name="cutoff">The cutoff timestamp; older items will be removed.</param>
        public void RemoveOlderThan(DateTime cutoff)
        {
            lock (_lock)
            {
                // FIFO is chronological because TemporalItem<T>.Create enforces monotonic timestamps.
                while (_queue.TryPeek(out var item) && item.Timestamp < cutoff)
                {
                    _queue.TryDequeue(out _);
                }
            }
        }

        /// <summary>
        /// Returns the total time span covered by the items in the queue,
        /// calculated as the difference between the latest and earliest timestamps.
        /// Returns <see cref="TimeSpan.Zero"/> if the queue is empty or has a single item.
        /// </summary>
        public TimeSpan GetTimeSpan()
        {
            lock (_lock)
            {
                if (_queue.IsEmpty) return TimeSpan.Zero;

                // Build a snapshot to compute min/max deterministically.
                var items = _queue.ToList();
                if (items.Count < 2) return TimeSpan.Zero;

                var min = items[0].Timestamp;
                var max = items[0].Timestamp;
                for (int i = 1; i < items.Count; i++)
                {
                    var ts = items[i].Timestamp;
                    if (ts < min) min = ts;
                    if (ts > max) max = ts;
                }
                var span = max - min;
                return span < TimeSpan.Zero ? TimeSpan.Zero : span;
            }
        }

        /// <summary>
        /// Counts the number of items whose timestamps fall within the specified range (inclusive).
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when <paramref name="to"/> is earlier than <paramref name="from"/>.</exception>
        public int CountInRange(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("to must be >= from", nameof(to));

            lock (_lock)
            {
                return _queue.Count(i => i.Timestamp >= from && i.Timestamp <= to);
            }
        }

        /// <summary>
        /// Removes all items from the queue.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                // Use Clear() when available; otherwise, drain the queue.
                _queue.Clear();
                // Fallback for older TFMs:
                // while (_queue.TryDequeue(out _)) { }
            }
        }

        /// <summary>
        /// Removes all items whose timestamps fall within the specified inclusive range.
        /// This method takes a snapshot, filters, clears the queue, then enqueues kept items.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown when <paramref name="to"/> is earlier than <paramref name="from"/>.</exception>
        public void RemoveRange(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("to must be >= from", nameof(to));

            lock (_lock)
            {
                var kept = _queue.Where(i => i.Timestamp < from || i.Timestamp > to).ToList();

                _queue.Clear(); // .NET 6+; otherwise drain with TryDequeue in a loop
                foreach (var item in kept)
                    _queue.Enqueue(item);
            }
        }

        /// <summary>
        /// Returns the most recent item in the queue, or null if the queue is empty.
        /// </summary>
        public TemporalItem<T>? GetLatest()
        {
            lock (_lock)
            {
                // If we needed O(1), we could track tail timestamp separately; O(n) is acceptable here.
                TemporalItem<T>? best = null;
                DateTime bestTs = DateTime.MinValue;

                foreach (var it in _queue)
                {
                    if (it.Timestamp > bestTs)
                    {
                        bestTs = it.Timestamp;
                        best = it;
                    }
                }
                return best;
            }
        }

        /// <summary>
        /// Returns the earliest item in the queue, or null if the queue is empty.
        /// </summary>
        public TemporalItem<T>? GetEarliest()
        {
            lock (_lock)
            {
                TemporalItem<T>? best = null;
                DateTime bestTs = DateTime.MaxValue;

                foreach (var it in _queue)
                {
                    if (it.Timestamp < bestTs)
                    {
                        bestTs = it.Timestamp;
                        best = it;
                    }
                }
                return best;
            }
        }

        /// <summary>
        /// Returns all items with timestamps strictly earlier than the specified time.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetBefore(DateTime time)
        {
            lock (_lock)
            {
                return _queue.Where(i => i.Timestamp < time)
                             .ToList();
            }
        }

        /// <summary>
        /// Returns all items with timestamps strictly later than the specified time.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetAfter(DateTime time)
        {
            lock (_lock)
            {
                return _queue.Where(i => i.Timestamp > time)
                             .ToList();
            }
        }
    }
}