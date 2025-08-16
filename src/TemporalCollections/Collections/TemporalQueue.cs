// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;
using TemporalCollections.Utilities;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe FIFO queue where each item has an insertion timestamp,
    /// enabling efficient time-based queries and removal of old elements.
    /// Public API uses DateTime; internal comparisons use DateTimeOffset (UTC).
    /// </summary>
    /// <typeparam name="T">Type of items stored in the queue.</typeparam>
    public class TemporalQueue<T> : ITimeQueryable<T>
    {
        // Oldest -> front ; Newest -> back
        private readonly Queue<TemporalItem<T>> _queue = new();
        private readonly Lock _lock = new();

        // Centralized policy for DateTimeKind.Unspecified handling.
        private const UnspecifiedPolicy DefaultPolicy = UnspecifiedPolicy.AssumeUtc;

        /// <summary>
        /// Gets the total number of items currently in the queue (O(1)).
        /// </summary>
        public int Count
        {
            get 
            { 
                lock (_lock) return _queue.Count; 
            }
        }

        /// <summary>
        /// Enqueues an item with the current timestamp.
        /// </summary>
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
        public TemporalItem<T> Dequeue()
        {
            lock (_lock)
            {
                if (_queue.Count == 0)
                    throw new InvalidOperationException("Queue is empty.");
                return _queue.Dequeue();
            }
        }

        /// <summary>
        /// Returns the item at the front of the queue without removing it.
        /// </summary>
        public TemporalItem<T> Peek()
        {
            lock (_lock)
            {
                if (_queue.Count == 0)
                    throw new InvalidOperationException("Queue is empty.");
                return _queue.Peek();
            }
        }

        /// <summary>
        /// Retrieves all items whose timestamps are within the specified inclusive time range.
        /// Snapshot taken under lock for consistent semantics.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to)
        {
            var (fromUtc, toUtc) = TimeNormalization.NormalizeRange(from, to, unspecifiedPolicy: DefaultPolicy);
            long f = fromUtc.UtcTicks, t = toUtc.UtcTicks;

            lock (_lock)
            {
                if (_queue.Count == 0) return Array.Empty<TemporalItem<T>>();

                // Queue enumeration is oldest -> newest
                return _queue.Where(i =>
                {
                    long x = i.Timestamp.UtcTicks;
                    return f <= x && x <= t;
                }).ToList();
            }
        }

        /// <summary>
        /// Removes items from the front of the queue until all remaining items
        /// have timestamps equal to or newer than the cutoff.
        /// </summary>
        public void RemoveOlderThan(DateTime cutoff)
        {
            long c = TimeNormalization.UtcTicks(cutoff, DefaultPolicy);

            lock (_lock)
            {
                while (_queue.Count > 0 && _queue.Peek().Timestamp.UtcTicks < c)
                    _queue.Dequeue();
            }
        }

        /// <summary>
        /// Returns the total time span covered by the items in the queue.
        /// </summary>
        public TimeSpan GetTimeSpan()
        {
            lock (_lock)
            {
                if (_queue.Count < 2) return TimeSpan.Zero;

                // Compute min/max in a single pass
                DateTimeOffset min = DateTimeOffset.MaxValue;
                DateTimeOffset max = DateTimeOffset.MinValue;

                foreach (var item in _queue)
                {
                    var ts = item.Timestamp;
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
        public int CountInRange(DateTime from, DateTime to)
        {
            var (fromUtc, toUtc) = TimeNormalization.NormalizeRange(from, to, unspecifiedPolicy: DefaultPolicy);
            long f = fromUtc.UtcTicks, t = toUtc.UtcTicks;

            lock (_lock)
            {
                int count = 0;
                foreach (var i in _queue)
                {
                    long x = i.Timestamp.UtcTicks;
                    if (f <= x && x <= t) count++;
                }
                return count;
            }
        }

        /// <summary>
        /// Removes all items from the queue.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _queue.Clear();
            }
        }

        /// <summary>
        /// Removes all items whose timestamps fall within the inclusive range.
        /// Snapshot-filter-clear-reenqueue to preserve FIFO among kept elements.
        /// </summary>
        public void RemoveRange(DateTime from, DateTime to)
        {
            var (fromUtc, toUtc) = TimeNormalization.NormalizeRange(from, to, unspecifiedPolicy: DefaultPolicy);
            long f = fromUtc.UtcTicks, t = toUtc.UtcTicks;

            lock (_lock)
            {
                if (_queue.Count == 0) return;

                var kept = new List<TemporalItem<T>>(_queue.Count);
                foreach (var i in _queue)
                {
                    long x = i.Timestamp.UtcTicks;
                    if (x < f || x > t) kept.Add(i);
                }

                _queue.Clear();
                foreach (var i in kept) _queue.Enqueue(i);
            }
        }

        /// <summary>
        /// Returns the most recent item in the queue, or null if the queue is empty.
        /// </summary>
        public TemporalItem<T>? GetLatest()
        {
            lock (_lock)
            {
                if (_queue.Count == 0) return null;

                TemporalItem<T>? best = null;
                long bestTicks = long.MinValue;

                foreach (var it in _queue)
                {
                    long x = it.Timestamp.UtcTicks;
                    if (x > bestTicks) { bestTicks = x; best = it; }
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
                if (_queue.Count == 0) return null;

                // The front is the earliest because we enqueue in chronological order
                return _queue.Peek();
            }
        }

        /// <summary>
        /// Returns all items with timestamps strictly earlier than the specified time.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetBefore(DateTime time)
        {
            long cutoff = TimeNormalization.UtcTicks(time, DefaultPolicy);

            lock (_lock)
            {
                if (_queue.Count == 0) return [];

                return _queue
                    .Where(i => i.Timestamp.UtcTicks < cutoff)
                    .ToList();
            }
        }

        /// <summary>
        /// Returns all items with timestamps strictly later than the specified time.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetAfter(DateTime time)
        {
            long cutoff = TimeNormalization.UtcTicks(time, DefaultPolicy);

            lock (_lock)
            {
                if (_queue.Count == 0) return [];

                return _queue
                    .Where(i => i.Timestamp.UtcTicks > cutoff)
                    .ToList();
            }
        }
    }
}