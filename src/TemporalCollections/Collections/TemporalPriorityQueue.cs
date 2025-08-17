// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;
using TemporalCollections.Utilities;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe priority queue where each item has a priority and insertion timestamp,
    /// allowing ordering by priority and stable ordering by insertion time.
    /// Implements <see cref="ITimeQueryable{TValue}"/> for time-based querying and removal.
    /// Public API uses DateTime; internal comparisons use DateTimeOffset (UTC).
    /// </summary>
    /// <typeparam name="TPriority">Type of the priority; must implement <see cref="IComparable{TPriority}"/>.</typeparam>
    /// <typeparam name="TValue">Type of the stored values.</typeparam>
    public class TemporalPriorityQueue<TPriority, TValue> : ITimeQueryable<TValue>
        where TPriority : IComparable<TPriority>
    {
        private readonly Lock _lock = new();
        private readonly SortedSet<QueueItem> _set;

        // Centralized policy for DateTimeKind.Unspecified handling.
        private const UnspecifiedPolicy DefaultPolicy = UnspecifiedPolicy.AssumeUtc;

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
                lock (_lock) return _set.Count; 
            }
        }

        /// <summary>
        /// Enqueues a value with the specified priority and current timestamp.
        /// </summary>
        public void Enqueue(TValue value, TPriority priority)
        {
            // TemporalItem.Create ensures strictly increasing DateTimeOffset UTC timestamps.
            var ti = TemporalItem<TValue>.Create(value);
            lock (_lock)
            {
                _set.Add(new QueueItem(ti.Value, priority, ti.Timestamp));
            }
        }

        /// <summary>
        /// Attempts to dequeue the highest priority item (lowest priority value).
        /// </summary>
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
        public IEnumerable<TemporalItem<TValue>> GetInRange(DateTime from, DateTime to)
        {
            var (fromUtc, toUtc) = TimeNormalization.NormalizeRange(from, to, unspecifiedPolicy: DefaultPolicy);
            long f = fromUtc.UtcTicks, t = toUtc.UtcTicks;

            lock (_lock)
            {
                return _set
                    .Where(item => f <= item.Timestamp.UtcTicks && item.Timestamp.UtcTicks <= t)
                    .Select(i => new TemporalItem<TValue>(i.Value, i.Timestamp))
                    .OrderBy(i => i.Timestamp.UtcTicks)
                    .ToList();
            }
        }

        /// <summary>
        /// Removes all items older than the specified cutoff date (strictly less).
        /// </summary>
        public void RemoveOlderThan(DateTime cutoff)
        {
            long c = TimeNormalization.UtcTicks(cutoff, DefaultPolicy);

            lock (_lock)
            {
                // Cannot break early: set is ordered by (Priority, Timestamp), not only by Timestamp.
                var toRemove = new List<QueueItem>();
                foreach (var item in _set)
                {
                    if (item.Timestamp.UtcTicks < c)
                        toRemove.Add(item);
                }
                foreach (var item in toRemove)
                    _set.Remove(item);
            }
        }

        /// <summary>
        /// Returns the time span between the earliest and latest timestamps in the queue.
        /// Returns <see cref="TimeSpan.Zero"/> if the queue has fewer than two items.
        /// </summary>
        public TimeSpan GetTimeSpan()
        {
            lock (_lock)
            {
                if (_set.Count < 2) return TimeSpan.Zero;

                DateTimeOffset minTs = DateTimeOffset.MaxValue;
                DateTimeOffset maxTs = DateTimeOffset.MinValue;

                foreach (var item in _set)
                {
                    var ts = item.Timestamp;
                    if (ts < minTs) minTs = ts;
                    if (ts > maxTs) maxTs = ts;
                }

                var span = maxTs - minTs;
                return span < TimeSpan.Zero ? TimeSpan.Zero : span;
            }
        }

        /// <summary>
        /// Counts the number of items with timestamps within the inclusive range [from, to].
        /// </summary>
        public int CountInRange(DateTime from, DateTime to)
        {
            var (fromUtc, toUtc) = TimeNormalization.NormalizeRange(from, to, unspecifiedPolicy: DefaultPolicy);
            long f = fromUtc.UtcTicks, t = toUtc.UtcTicks;

            lock (_lock)
            {
                int count = 0;
                foreach (var i in _set)
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
            lock (_lock) _set.Clear();
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
                var toRemove = _set.Where(i =>
                {
                    long x = i.Timestamp.UtcTicks;
                    return f <= x && x <= t;
                }).ToList();

                foreach (var item in toRemove)
                    _set.Remove(item);
            }
        }

        /// <summary>
        /// Gets the most recent item by timestamp, or null if the queue is empty.
        /// </summary>
        public TemporalItem<TValue>? GetLatest()
        {
            lock (_lock)
            {
                if (_set.Count == 0) return null;
                QueueItem? latest = null;

                foreach (var item in _set)
                {
                    if (latest is null || item.Timestamp.UtcTicks > latest.Timestamp.UtcTicks)
                        latest = item;
                }

                return latest is null ? null : new TemporalItem<TValue>(latest.Value, latest.Timestamp);
            }
        }

        /// <summary>
        /// Gets the oldest item by timestamp, or null if the queue is empty.
        /// </summary>
        public TemporalItem<TValue>? GetEarliest()
        {
            lock (_lock)
            {
                if (_set.Count == 0) return null;
                QueueItem? earliest = null;

                foreach (var item in _set)
                {
                    if (earliest is null || item.Timestamp.UtcTicks < earliest.Timestamp.UtcTicks)
                        earliest = item;
                }

                return earliest is null ? null : new TemporalItem<TValue>(earliest.Value, earliest.Timestamp);
            }
        }

        /// <summary>
        /// Returns all items strictly before the specified time.
        /// </summary>
        public IEnumerable<TemporalItem<TValue>> GetBefore(DateTime time)
        {
            long cutoff = TimeNormalization.UtcTicks(time, DefaultPolicy);

            lock (_lock)
            {
                return _set
                    .Where(i => i.Timestamp.UtcTicks < cutoff)
                    .Select(i => new TemporalItem<TValue>(i.Value, i.Timestamp))
                    .OrderBy(i => i.Timestamp.UtcTicks)
                    .ToList();
            }
        }

        /// <summary>
        /// Returns all items strictly after the specified time.
        /// </summary>
        public IEnumerable<TemporalItem<TValue>> GetAfter(DateTime time)
        {
            long cutoff = TimeNormalization.UtcTicks(time, DefaultPolicy);

            lock (_lock)
            {
                return _set
                    .Where(i => i.Timestamp.UtcTicks > cutoff)
                    .Select(i => new TemporalItem<TValue>(i.Value, i.Timestamp))
                    .OrderBy(i => i.Timestamp.UtcTicks)
                    .ToList();
            }
        }

        /// <summary>
        /// Counts the number of items with timestamp greater than or equal to the specified cutoff.
        /// </summary>
        public int CountSince(DateTime from)
        {
            long f = TimeNormalization.UtcTicks(from, DefaultPolicy);

            lock (_lock)
            {
                int count = 0;
                foreach (var item in _set)
                {
                    if (item.Timestamp.UtcTicks >= f)
                        count++;
                }
                return count;
            }
        }

        /// <summary>
        /// Internal record representing a queue item with priority and timestamp.
        /// </summary>
        private record QueueItem : TemporalItem<TValue>, IComparable<QueueItem>
        {
            /// <summary>Gets the priority of the item.</summary>
            public TPriority Priority { get; }

            /// <summary>
            /// Initializes a new instance of the <see cref="QueueItem"/> record.
            /// </summary>
            public QueueItem(TValue value, TPriority priority, DateTimeOffset timestamp)
                : base(value, timestamp)
            {
                Priority = priority;
            }

            /// <summary>
            /// Compares this item to another based on priority, then timestamp, then runtime id for strict ordering.
            /// </summary>
            public int CompareTo(QueueItem? other)
            {
                if (other is null) return 1;

                int c = Priority.CompareTo(other.Priority);
                if (c != 0) return c;

                c = Timestamp.CompareTo(other.Timestamp);
                if (c != 0) return c;

                // Tie-breaker: ensure strict weak ordering to avoid SortedSet dropping distinct items
                int hx = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(this);
                int hy = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(other);
                return hx.CompareTo(hy);
            }
        }

        /// <summary>
        /// Comparer for queue items delegating to <see cref="QueueItem.CompareTo"/>.
        /// </summary>
        private sealed class QueueItemComparer : IComparer<QueueItem>
        {
            public int Compare(QueueItem? x, QueueItem? y)
            {
                if (ReferenceEquals(x, y)) return 0;
                if (x is null) return -1;
                if (y is null) return 1;
                return x.CompareTo(y);
            }
        }
    }
}