// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;
using TemporalCollections.Utilities;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe fixed-capacity circular buffer that stores timestamped items,
    /// overwriting the oldest entries when full.
    /// Public API uses DateTime; internal comparisons use DateTimeOffset (UTC).
    /// </summary>
    /// <typeparam name="T">Type of items stored in the buffer.</typeparam>
    public class TemporalCircularBuffer<T> : ITimeQueryable<T>
    {
        private readonly Lock _lock = new();
        private readonly TemporalItem<T>[] _buffer;
        private int _head;
        private int _count;

        // Centralized policy for DateTimeKind.Unspecified handling.
        private const UnspecifiedPolicy DefaultPolicy = UnspecifiedPolicy.AssumeUtc;

        /// <summary>Gets the fixed capacity of the circular buffer.</summary>
        public int Capacity { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TemporalCircularBuffer{T}"/> class with the specified capacity.
        /// </summary>
        /// <exception cref="ArgumentException">Thrown if capacity is not positive.</exception>
        public TemporalCircularBuffer(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentException("Capacity must be positive", nameof(capacity));

            Capacity = capacity;
            _buffer = new TemporalItem<T>[capacity];
            _head = 0;
            _count = 0;
        }

        /// <summary>Gets the current number of items stored in the buffer.</summary>
        public int Count
        {
            get { lock (_lock) return _count; }
        }

        /// <summary>
        /// Adds a new item to the buffer with the current timestamp,
        /// overwriting the oldest item if the buffer is full.
        /// </summary>
        public void Add(T item)
        {
            var temporalItem = TemporalItem<T>.Create(item);

            lock (_lock)
            {
                _buffer[_head] = temporalItem;
                _head = (_head + 1) % Capacity;

                if (_count < Capacity)
                    _count++;
            }
        }

        /// <summary>
        /// Returns a snapshot list of the buffer contents ordered from the oldest to the most recent item.
        /// </summary>
        public IList<TemporalItem<T>> GetSnapshot()
        {
            lock (_lock)
            {
                var result = new List<TemporalItem<T>>(_count);
                int start = (_head - _count + Capacity) % Capacity;

                for (int i = 0; i < _count; i++)
                {
                    int idx = (start + i) % Capacity;
                    result.Add(_buffer[idx]);
                }

                return result;
            }
        }

        /// <summary>
        /// Returns all temporal items whose timestamps fall within the specified time range, inclusive.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to)
        {
            var (fromUtc, toUtc) = TimeNormalization.NormalizeRange(from, to, nameof(from), nameof(to), DefaultPolicy);
            long f = fromUtc.UtcTicks, t = toUtc.UtcTicks;

            lock (_lock)
            {
                if (_count == 0) return Array.Empty<TemporalItem<T>>();

                var list = new List<TemporalItem<T>>();
                IterateOrdered(item =>
                {
                    long x = item.Timestamp.UtcTicks;
                    if (f <= x && x <= t)
                        list.Add(item);
                    // Fast-path possibile: items cronologici → si potrebbe interrompere quando x > t.
                    // Evitiamo early-break per mantenere IterateOrdered semplice e senza effetti collaterali.
                });
                return list;
            }
        }

        /// <summary>
        /// Removes all items with timestamps older than the specified cutoff time.
        /// Retains only items with timestamps >= cutoff.
        /// </summary>
        public void RemoveOlderThan(DateTime cutoff)
        {
            long c = TimeNormalization.UtcTicks(cutoff, DefaultPolicy);

            lock (_lock)
            {
                if (_count == 0) return;

                var kept = new List<TemporalItem<T>>(_count);
                IterateOrdered(item =>
                {
                    if (item.Timestamp.UtcTicks >= c)
                        kept.Add(item);
                });

                RebuildFromOrdered(kept);
            }
        }

        /// <summary>
        /// Returns the total timespan covered by items in the buffer,
        /// computed as (latest.Timestamp - earliest.Timestamp).
        /// Returns TimeSpan.Zero if there are fewer than two items.
        /// </summary>
        public TimeSpan GetTimeSpan()
        {
            lock (_lock)
            {
                if (_count < 2) return TimeSpan.Zero;

                var earliest = GetAtOrderedIndex(0);
                var latest = GetAtOrderedIndex(_count - 1);

                var span = latest.Timestamp - earliest.Timestamp; // DateTimeOffset subtraction -> TimeSpan
                return span < TimeSpan.Zero ? TimeSpan.Zero : span;
            }
        }

        /// <summary>
        /// Returns the number of items with timestamps in the inclusive range [from, to].
        /// </summary>
        public int CountInRange(DateTime from, DateTime to)
        {
            var (fromUtc, toUtc) = TimeNormalization.NormalizeRange(from, to, nameof(from), nameof(to), DefaultPolicy);
            long f = fromUtc.UtcTicks, t = toUtc.UtcTicks;

            lock (_lock)
            {
                if (_count == 0) return 0;

                int cnt = 0;
                IterateOrdered(item =>
                {
                    long x = item.Timestamp.UtcTicks;
                    if (f <= x && x <= t) cnt++;
                });
                return cnt;
            }
        }

        /// <summary>
        /// Removes all items from the buffer.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                Array.Clear(_buffer, 0, _buffer.Length);
                _head = 0;
                _count = 0;
            }
        }

        /// <summary>
        /// Removes all items whose timestamps fall within the inclusive range [from, to].
        /// </summary>
        public void RemoveRange(DateTime from, DateTime to)
        {
            var (fromUtc, toUtc) = TimeNormalization.NormalizeRange(from, to, nameof(from), nameof(to), DefaultPolicy);
            long f = fromUtc.UtcTicks, t = toUtc.UtcTicks;

            lock (_lock)
            {
                if (_count == 0) return;

                var kept = new List<TemporalItem<T>>(_count);
                IterateOrdered(item =>
                {
                    long x = item.Timestamp.UtcTicks;
                    if (x < f || x > t)
                        kept.Add(item);
                });

                RebuildFromOrdered(kept);
            }
        }

        /// <summary>
        /// Retrieves the latest (most recent) item, or null if the buffer is empty.
        /// </summary>
        public TemporalItem<T>? GetLatest()
        {
            lock (_lock)
            {
                if (_count == 0) return null;
                int lastIdx = (_head - 1 + Capacity) % Capacity;
                return _buffer[lastIdx];
            }
        }

        /// <summary>
        /// Retrieves the earliest (oldest) item, or null if the buffer is empty.
        /// </summary>
        public TemporalItem<T>? GetEarliest()
        {
            lock (_lock)
            {
                if (_count == 0) return null;
                int firstIdx = (_head - _count + Capacity) % Capacity;
                return _buffer[firstIdx];
            }
        }

        /// <summary>
        /// Retrieves all items with timestamp strictly before <paramref name="time"/>.
        /// The returned items are ordered from oldest to newest.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetBefore(DateTime time)
        {
            long cutoff = TimeNormalization.UtcTicks(time, DefaultPolicy);

            lock (_lock)
            {
                if (_count == 0) return Array.Empty<TemporalItem<T>>();

                var list = new List<TemporalItem<T>>();
                IterateOrdered(item =>
                {
                    if (item.Timestamp.UtcTicks < cutoff)
                        list.Add(item);
                });
                return list;
            }
        }

        /// <summary>
        /// Retrieves all items with timestamp strictly after <paramref name="time"/>.
        /// The returned items are ordered from oldest to newest.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetAfter(DateTime time)
        {
            long cutoff = TimeNormalization.UtcTicks(time, DefaultPolicy);

            lock (_lock)
            {
                if (_count == 0) return Array.Empty<TemporalItem<T>>();

                var list = new List<TemporalItem<T>>();
                IterateOrdered(item =>
                {
                    if (item.Timestamp.UtcTicks > cutoff)
                        list.Add(item);
                });
                return list;
            }
        }

        #region Internal helpers

        /// <summary>
        /// Iterates items in chronological order (oldest to newest) and invokes the provided action.
        /// Caller must hold the lock.
        /// </summary>
        private void IterateOrdered(Action<TemporalItem<T>> action)
        {
            int start = (_head - _count + Capacity) % Capacity;
            for (int i = 0; i < _count; i++)
            {
                int idx = (start + i) % Capacity;
                action(_buffer[idx]);
            }
        }

        /// <summary>
        /// Gets the item at the given logical ordered position (0.._count-1), oldest-first.
        /// Caller must hold the lock.
        /// </summary>
        private TemporalItem<T> GetAtOrderedIndex(int orderedIndex)
        {
            int start = (_head - _count + Capacity) % Capacity;
            int idx = (start + orderedIndex) % Capacity;
            return _buffer[idx];
        }

        /// <summary>
        /// Rebuilds the ring buffer from an already ordered (oldest-to-newest) list.
        /// Caller must hold the lock.
        /// </summary>
        private void RebuildFromOrdered(List<TemporalItem<T>> orderedItems)
        {
            Array.Clear(_buffer, 0, _buffer.Length);

            _count = Math.Min(orderedItems.Count, Capacity);
            _head = _count % Capacity;

            for (int i = 0; i < _count; i++)
                _buffer[i] = orderedItems[i];
        }

        #endregion
    }
}