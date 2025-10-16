// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe FIFO queue where each item has an insertion timestamp,
    /// enabling efficient time-based queries and removal of old elements.
    /// Public API uses DateTime; internal comparisons use DateTimeOffset (UTC).
    /// </summary>
    /// <typeparam name="T">Type of items stored in the queue.</typeparam>
    public class TemporalQueue<T> : TimeQueryableBase<T>
    {
        // Oldest -> front ; Newest -> back
        private readonly Queue<TemporalItem<T>> _queue = new();
        private readonly Lock _lock = new();

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
        public override IEnumerable<TemporalItem<T>> GetInRange(DateTimeOffset from, DateTimeOffset to)
        {
            if (from > to) throw new ArgumentException("'from' must be <= 'to'.");

            long f = from.UtcTicks, t = to.UtcTicks;
            if (f > t) (f, t) = (t, f);

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
        public override void RemoveOlderThan(DateTimeOffset cutoff)
        {
            long c = cutoff.UtcTicks;

            lock (_lock)
            {
                while (_queue.Count > 0 && _queue.Peek().Timestamp.UtcTicks < c)
                    _queue.Dequeue();
            }
        }

        /// <summary>
        /// Returns the total time span covered by the items in the queue.
        /// </summary>
        public override TimeSpan GetTimeSpan()
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
        public override int CountInRange(DateTimeOffset from, DateTimeOffset to)
        {
            if (from > to) throw new ArgumentException("'from' must be <= 'to'.");

            long f = from.UtcTicks, t = to.UtcTicks;
            if (f > t) (f, t) = (t, f);

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
        public override void Clear()
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
        public override void RemoveRange(DateTimeOffset from, DateTimeOffset to)
        {
            if (from > to) throw new ArgumentException("'from' must be <= 'to'.");

            long f = from.UtcTicks, t = to.UtcTicks;
            if (f > t) (f, t) = (t, f);

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
        public override TemporalItem<T>? GetLatest()
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
        public override TemporalItem<T>? GetEarliest()
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
        public override IEnumerable<TemporalItem<T>> GetBefore(DateTimeOffset time)
        {
            long cutoff = time.UtcTicks;

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
        public override IEnumerable<TemporalItem<T>> GetAfter(DateTimeOffset time)
        {
            long cutoff = time.UtcTicks;

            lock (_lock)
            {
                if (_queue.Count == 0) return [];

                return _queue
                    .Where(i => i.Timestamp.UtcTicks > cutoff)
                    .ToList();
            }
        }

        /// <summary>
        /// Counts the number of items with timestamp greater than or equal to the specified cutoff.
        /// </summary>
        public override int CountSince(DateTimeOffset from)
        {
            long f = from.UtcTicks;

            lock (_lock)
            {
                int count = 0;
                foreach (var item in _queue)
                {
                    if (item.Timestamp.UtcTicks >= f)
                        count++;
                }
                return count;
            }
        }

        /// <summary>
        /// Returns the item whose timestamp is closest to <paramref name="time"/>.
        /// If the queue is empty, returns <c>null</c>.
        /// In case of a tie (same distance before/after), the later item (timestamp ≥ time) is returned.
        /// Complexity: O(n) due to snapshot copy, then O(log n) search.
        /// </summary>
        public override TemporalItem<T>? GetNearest(DateTimeOffset time)
        {
            long target = time.UtcTicks;

            // Take a snapshot to avoid holding the lock during the search
            TemporalItem<T>[] snapshot;
            lock (_lock)
            {
                if (_queue.Count == 0) return null;
                snapshot = _queue.ToArray(); // oldest -> newest (timestamps ascending)
            }

            int n = snapshot.Length;
            int idx = LowerBound(snapshot, target); // first index with ts >= target

            if (idx == 0) return snapshot[0];
            if (idx == n) return snapshot[n - 1];

            long beforeDiff = target - snapshot[idx - 1].Timestamp.UtcTicks; // >= 0
            long afterDiff = snapshot[idx].Timestamp.UtcTicks - target;     // >= 0

            // Tie-break: prefer the later item (>= time)
            return (afterDiff <= beforeDiff) ? snapshot[idx] : snapshot[idx - 1];
        }

        #region Internal helpers

        /// <summary>
        /// Finds the index of the first element in <paramref name="arr"/> whose
        /// Timestamp.UtcTicks is greater than or equal to <paramref name="targetTicks"/>.
        /// Returns <c>arr.Length</c> if no such element exists.
        /// </summary>
        private static int LowerBound(TemporalItem<T>[] arr, long targetTicks)
        {
            int l = 0, r = arr.Length; // [l, r)
            while (l < r)
            {
                int m = (l + r) >> 1;
                long mid = arr[m].Timestamp.UtcTicks;
                if (mid < targetTicks) l = m + 1;
                else r = m;
            }
            return l;
        }

        #endregion
    }
}