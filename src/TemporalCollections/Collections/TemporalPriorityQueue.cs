// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe priority queue where each item has a priority and insertion timestamp,
    /// allowing ordering by priority and stable ordering by insertion time.
    /// Implements <see cref="ITimeQueryable{TValue}"/> for time-based querying and removal.
    /// </summary>
    /// <typeparam name="TPriority">Type of the priority; must implement <see cref="IComparable{TPriority}"/>.</typeparam>
    /// <typeparam name="TValue">Type of the stored values.</typeparam>
    public class TemporalPriorityQueue<TPriority, TValue> : ITimeQueryable<TValue>
        where TPriority : IComparable<TPriority>
    {
        private readonly Lock _lock = new();
        private readonly SortedSet<QueueItem> _set;

        /// <summary>
        /// Initializes a new instance of the <see cref="TemporalPriorityQueue{TPriority, TValue}"/> class.
        /// </summary>
        public TemporalPriorityQueue()
        {
            _set = new SortedSet<QueueItem>(new QueueItemComparer());
        }

        /// <summary>
        /// Gets the number of items currently in the queue.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _set.Count;
                }
            }
        }

        /// <summary>
        /// Enqueues a value with the specified priority and current timestamp.
        /// </summary>
        /// <param name="value">The value to enqueue.</param>
        /// <param name="priority">The priority associated with the value.</param>
        public void Enqueue(TValue value, TPriority priority)
        {
            var item = new QueueItem(value, priority, DateTime.UtcNow);
            lock (_lock)
            {
                _set.Add(item);
            }
        }

        /// <summary>
        /// Attempts to dequeue the highest priority item (lowest priority value).
        /// </summary>
        /// <param name="value">When this method returns, contains the dequeued value if successful; otherwise, the default value.</param>
        /// <returns>True if an item was dequeued; otherwise, false if the queue was empty.</returns>
        public bool TryDequeue(out TValue? value)
        {
            lock (_lock)
            {
                if (_set.Count == 0)
                {
                    value = default;
                    return false;
                }

                var first = _set.Min!;
                _set.Remove(first);
                value = first.Value;
                return true;
            }
        }

        /// <summary>
        /// Attempts to peek at the highest priority item without removing it.
        /// </summary>
        /// <param name="value">When this method returns, contains the peeked value if successful; otherwise, the default value.</param>
        /// <returns>True if an item was found; otherwise, false if the queue is empty.</returns>
        public bool TryPeek(out TValue? value)
        {
            lock (_lock)
            {
                if (_set.Count == 0)
                {
                    value = default;
                    return false;
                }

                value = _set.Min!.Value;
                return true;
            }
        }

        /// <summary>
        /// Returns items whose timestamps fall within the given range (inclusive).
        /// </summary>
        /// <param name="from">Start of the time range (inclusive).</param>
        /// <param name="to">End of the time range (inclusive).</param>
        /// <returns>An enumerable of temporal items in the specified time range.</returns>
        public IEnumerable<TemporalItem<TValue>> GetInRange(DateTime from, DateTime to)
        {
            List<TemporalItem<TValue>> results;
            lock (_lock)
            {
                results = _set
                    .Where(item => item.Timestamp >= from && item.Timestamp <= to)
                    .Cast<TemporalItem<TValue>>()
                    .ToList();
            }
            return results;
        }

        /// <summary>
        /// Removes all items older than the specified cutoff date.
        /// </summary>
        /// <param name="cutoff">The cutoff timestamp; items older than this will be removed.</param>
        public void RemoveOlderThan(DateTime cutoff)
        {
            lock (_lock)
            {
                var toRemove = new List<QueueItem>();

                foreach (var item in _set)
                {
                    if (item.Timestamp < cutoff)
                        toRemove.Add(item);
                    else
                        break; // since sorted by timestamp as tiebreaker, can stop early
                }

                foreach (var item in toRemove)
                {
                    _set.Remove(item);
                }
            }
        }

        /// <summary>
        /// Internal record representing a queue item with priority and timestamp.
        /// </summary>
        private record QueueItem : TemporalItem<TValue>, IComparable<QueueItem>
        {
            /// <summary>
            /// Gets the priority of the item.
            /// </summary>
            public TPriority Priority { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="QueueItem"/> record.
            /// </summary>
            /// <param name="value">The stored value.</param>
            /// <param name="priority">The priority of the item.</param>
            /// <param name="timestamp">The insertion timestamp.</param>
            public QueueItem(TValue value, TPriority priority, DateTime timestamp)
                : base(value, timestamp)
            {
                Priority = priority;
            }

            /// <summary>
            /// Compares this item to another based on priority, then timestamp.
            /// </summary>
            /// <param name="other">The other item to compare to.</param>
            /// <returns>A signed integer that indicates the relative order of the objects being compared.</returns>
            public int CompareTo(QueueItem? other)
            {
                if (other == null) return 1;

                int priorityComparison = Priority.CompareTo(other.Priority);
                if (priorityComparison != 0)
                    return priorityComparison;

                return Timestamp.CompareTo(other.Timestamp);
            }
        }

        /// <summary>
        /// Comparer for queue items that delegates to their <see cref="QueueItem.CompareTo"/> method.
        /// </summary>
        private class QueueItemComparer : IComparer<QueueItem>
        {
            /// <summary>
            /// Compares two queue items.
            /// </summary>
            /// <param name="x">The first item to compare.</param>
            /// <param name="y">The second item to compare.</param>
            /// <returns>A signed integer that indicates the relative order of the objects being compared.</returns>
            public int Compare(QueueItem? x, QueueItem? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x == null) return -1;
                if (y == null) return 1;
                return x.CompareTo(y);
            }
        }
    }
}