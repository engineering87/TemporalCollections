// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe fixed-capacity circular buffer that stores timestamped items,
    /// overwriting the oldest entries when full.
    /// </summary>
    /// <typeparam name="T">Type of items stored in the buffer.</typeparam>
    public class TemporalCircularBuffer<T> : ITimeQueryable<T>
    {
        private readonly Lock _lock = new();
        private readonly TemporalItem<T>[] _buffer;
        private int _head;
        private int _count;

        /// <summary>
        /// Gets the fixed capacity of the circular buffer.
        /// </summary>
        public int Capacity { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="TemporalCircularBuffer{T}"/> class with the specified capacity.
        /// </summary>
        /// <param name="capacity">The fixed maximum number of elements the buffer can hold.</param>
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

        /// <summary>
        /// Gets the current number of items stored in the buffer.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _count;
                }
            }
        }

        /// <summary>
        /// Adds a new item to the buffer with the current timestamp,
        /// overwriting the oldest item if the buffer is full.
        /// </summary>
        /// <param name="item">The item to add.</param>
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
        /// <param name="from">The start of the time range (inclusive).</param>
        /// <param name="to">The end of the time range (inclusive).</param>
        /// <returns>An enumerable of temporal items with timestamps between <paramref name="from"/> and <paramref name="to"/>.</returns>
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to)
        {
            lock (_lock)
            {
                return GetSnapshot()
                    .Where(item => item.Timestamp >= from && item.Timestamp <= to)
                    .ToList();
            }
        }

        /// <summary>
        /// Removes all items with timestamps older than the specified cutoff time.
        /// Retains only items with timestamps greater than or equal to <paramref name="cutoff"/>.
        /// </summary>
        /// <param name="cutoff">The cutoff <see cref="DateTime"/>; items older than this will be removed.</param>
        public void RemoveOlderThan(DateTime cutoff)
        {
            lock (_lock)
            {
                var items = GetSnapshot()
                    .Where(item => item.Timestamp >= cutoff)
                    .ToArray();

                _count = Math.Min(items.Length, Capacity);
                _head = _count % Capacity;

                Array.Clear(_buffer, 0, _buffer.Length);
                for (int i = 0; i < _count; i++)
                {
                    _buffer[i] = items[i];
                }
            }
        }
    }
}