// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe LIFO stack where each item carries an insertion timestamp (DateTimeOffset, UTC),
    /// enabling time-based queries and cleanups while keeping public method signatures in DateTime.
    /// </summary>
    /// <typeparam name="T">Type of the items stored in the stack.</typeparam>
    public class TemporalStack<T> : TimeQueryableBase<T>
    {
        private readonly List<TemporalItem<T>> _items = [];
        private readonly Lock _lock = new();

        /// <summary>
        /// Gets the number of items currently in the stack (O(1)).
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                    return _items.Count;
            }
        }

        /// <summary>
        /// Pushes a new item onto the stack, recording the current timestamp (UTC, monotonic).
        /// </summary>
        public void Push(T item)
        {
            var temporalItem = TemporalItem<T>.Create(item);
            lock (_lock)
                _items.Add(temporalItem);
        }

        /// <summary>
        /// Pops the most recently pushed item.
        /// </summary>
        public TemporalItem<T> Pop()
        {
            lock (_lock)
            {
                int n = _items.Count;
                if (n == 0) throw new InvalidOperationException("Stack is empty.");
                var item = _items[n - 1];
                _items.RemoveAt(n - 1);
                return item;
            }
        }

        /// <summary>
        /// Peeks the most recently pushed item without removing it.
        /// </summary>
        public TemporalItem<T> Peek()
        {
            lock (_lock)
            {
                int n = _items.Count;
                if (n == 0) throw new InvalidOperationException("Stack is empty.");
                return _items[n - 1];
            }
        }

        /// <summary>
        /// Retrieves all items whose timestamps are within the inclusive range [from, to].
        /// Returned items are ordered by ascending timestamp.
        /// </summary>
        public override IEnumerable<TemporalItem<T>> GetInRange(DateTimeOffset from, DateTimeOffset to)
        {
            if (from > to) throw new ArgumentException("'from' must be <= 'to'.");

            long f = from.UtcTicks;
            long t = to.UtcTicks;

            if (f > t)
                (f, t) = (t, f);

            lock (_lock)
            {
                if (_items.Count == 0) return [];

                return _items
                    .Where(i => f <= i.Timestamp.UtcTicks && i.Timestamp.UtcTicks <= t)
                    .OrderBy(i => i.Timestamp.UtcTicks)
                    .ToList();
            }
        }

        /// <summary>
        /// Removes all items with Timestamp &lt; cutoff (exclusive).
        /// Complexity: O(n).
        /// </summary>
        public override void RemoveOlderThan(DateTimeOffset cutoff)
        {
            long c = cutoff.UtcTicks;

            lock (_lock)
            {
                if (_items.Count == 0) return;

                // Preserve original relative order of kept items (stable filter).
                _items.RemoveAll(i => i.Timestamp.UtcTicks < c);
            }
        }

        /// <summary>
        /// Calculates the time span between the earliest and latest timestamps.
        /// Returns TimeSpan.Zero if fewer than two items.
        /// </summary>
        public override TimeSpan GetTimeSpan()
        {
            lock (_lock)
            {
                if (_items.Count < 2) return TimeSpan.Zero;

                var min = _items[0].Timestamp;
                var max = min;

                for (int i = 1; i < _items.Count; i++)
                {
                    var ts = _items[i].Timestamp;
                    if (ts < min) min = ts;
                    if (ts > max) max = ts;
                }

                var span = max - min;
                return span < TimeSpan.Zero ? TimeSpan.Zero : span;
            }
        }

        /// <summary>
        /// Counts items with timestamps in [from, to] (inclusive).
        /// </summary>
        public override int CountInRange(DateTimeOffset from, DateTimeOffset to)
        {
            long f = from.UtcTicks;
            long t = to.UtcTicks;

            if (f > t)
                (f, t) = (t, f);

            lock (_lock)
            {
                int count = 0;

                for (int i = 0; i < _items.Count; i++)
                {
                    long x = _items[i].Timestamp.UtcTicks;
                    if (f <= x && x <= t) count++;
                }
                return count;
            }
        }

        /// <summary>
        /// Removes all items.
        /// </summary>
        public override void Clear()
        {
            lock (_lock) _items.Clear();
        }

        /// <summary>
        /// Removes all items with timestamps in [from, to] (inclusive).
        /// </summary>
        public override void RemoveRange(DateTimeOffset from, DateTimeOffset to)
        {
            if (from > to) throw new ArgumentException("'from' must be <= 'to'.");

            long f = from.UtcTicks;
            long t = to.UtcTicks;

            if (f > t)
                (f, t) = (t, f);

            lock (_lock)
            {
                if (_items.Count == 0) return;
                _items.RemoveAll(i =>
                {
                    long x = i.Timestamp.UtcTicks;
                    return f <= x && x <= t;
                });
            }
        }

        /// <summary>
        /// Gets the most recent item by timestamp, or null if empty. O(n).
        /// </summary>
        public override TemporalItem<T>? GetLatest()
        {
            lock (_lock)
            {
                if (_items.Count == 0) return null;

                TemporalItem<T> best = _items[0];
                long bestTicks = best.Timestamp.UtcTicks;

                for (int i = 1; i < _items.Count; i++)
                {
                    long x = _items[i].Timestamp.UtcTicks;
                    if (x > bestTicks) { bestTicks = x; best = _items[i]; }
                }
                return best;
            }
        }

        /// <summary>
        /// Gets the earliest item by timestamp, or null if empty. O(n).
        /// </summary>
        public override TemporalItem<T>? GetEarliest()
        {
            lock (_lock)
            {
                if (_items.Count == 0) return null;

                TemporalItem<T> best = _items[0];
                long bestTicks = best.Timestamp.UtcTicks;

                for (int i = 1; i < _items.Count; i++)
                {
                    long x = _items[i].Timestamp.UtcTicks;
                    if (x < bestTicks) { bestTicks = x; best = _items[i]; }
                }
                return best;
            }
        }

        /// <summary>
        /// Gets all items strictly before <paramref name="time"/>, ordered by ascending timestamp.
        /// </summary>
        public override IEnumerable<TemporalItem<T>> GetBefore(DateTimeOffset time)
        {
            long cutoff = time.UtcTicks;

            lock (_lock)
            {
                if (_items.Count == 0) return [];

                return _items
                    .Where(i => i.Timestamp.UtcTicks < cutoff)
                    .OrderBy(i => i.Timestamp.UtcTicks)
                    .ToList();
            }
        }

        /// <summary>
        /// Gets all items strictly after <paramref name="time"/>, ordered by ascending timestamp.
        /// </summary>
        public override IEnumerable<TemporalItem<T>> GetAfter(DateTimeOffset time)
        {
            long cutoff = time.UtcTicks;

            lock (_lock)
            {
                if (_items.Count == 0) return [];

                return _items
                    .Where(i => i.Timestamp.UtcTicks > cutoff)
                    .OrderBy(i => i.Timestamp.UtcTicks)
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
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Timestamp.UtcTicks >= f)
                        count++;
                }
                return count;
            }
        }

        /// <summary>
        /// Returns the item whose timestamp is closest to <paramref name="time"/>.
        /// If the stack is empty, returns <c>null</c>.
        /// In case of a tie (equal distance before/after), the later item (>= time) is returned.
        /// Complexity: O(log n).
        /// </summary>
        public override TemporalItem<T>? GetNearest(DateTimeOffset time)
        {
            long target = time.UtcTicks;

            lock (_lock)
            {
                int n = _items.Count;
                if (n == 0) return null;

                // First index with ts >= target (or n if all < target)
                int idx = LowerBoundUtcTicks(target);

                if (idx == 0) return _items[0];
                if (idx == n) return _items[n - 1];

                long beforeDiff = target - _items[idx - 1].Timestamp.UtcTicks; // >= 0
                long afterDiff = _items[idx].Timestamp.UtcTicks - target;     // >= 0

                // Tie-break: prefer the later item (>= time)
                return (afterDiff <= beforeDiff) ? _items[idx] : _items[idx - 1];
            }
        }

        #region Internal helpers

        /// <summary>
        /// Finds the index of the first element with Timestamp.UtcTicks >= <paramref name="targetTicks"/>.
        /// Returns the count if no such element exists.
        /// </summary>
        private int LowerBoundUtcTicks(long targetTicks)
        {
            int left = 0;
            int right = _items.Count;
            while (left < right)
            {
                int mid = (left + right) >> 1;
                long midTicks = _items[mid].Timestamp.UtcTicks;
                if (midTicks < targetTicks)
                    left = mid + 1;
                else
                    right = mid;
            }
            return left;
        }

        #endregion
    }
}