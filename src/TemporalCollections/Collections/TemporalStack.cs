// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using System.Collections.Concurrent;
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe LIFO stack where each item has an insertion timestamp,
    /// enabling time-based queries and cleanups.
    /// </summary>
    /// <typeparam name="T">Type of the items stored in the stack.</typeparam>
    public class TemporalStack<T> : ITimeQueryable<T>
    {
        private readonly ConcurrentStack<TemporalItem<T>> _stack = new();
        private readonly Lock _lock = new();

        /// <summary>
        /// Gets the number of items currently in the stack (snapshot; O(n)).
        /// </summary>
        public int Count
        {
            get { lock (_lock) return _stack.Count; }
        }

        /// <summary>
        /// Pushes a new item onto the stack, recording the current timestamp.
        /// </summary>
        public void Push(T item)
        {
            var temporalItem = TemporalItem<T>.Create(item);
            lock (_lock) _stack.Push(temporalItem);
        }

        /// <summary> Pops the most recently pushed item. </summary>
        public TemporalItem<T> Pop()
        {
            lock (_lock)
            {
                if (_stack.TryPop(out var item)) return item;
                throw new InvalidOperationException("Stack is empty.");
            }
        }

        /// <summary> 
        /// Peeks the most recently pushed item without removing it. 
        /// </summary>
        public TemporalItem<T> Peek()
        {
            lock (_lock)
            {
                if (_stack.TryPeek(out var item)) return item;
                throw new InvalidOperationException("Stack is empty.");
            }
        }

        /// <summary>
        /// Retrieves all items whose timestamps are within the inclusive range [from, to].
        /// Returns items ordered by ascending timestamp.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to)
        {
            if (to < from) throw new ArgumentException("to must be >= from", nameof(to));
            lock (_lock)
            {
                return _stack
                    .Where(i => i.Timestamp >= from && i.Timestamp <= to)
                    .OrderBy(i => i.Timestamp)
                    .ToList();
            }
        }

        /// <summary>
        /// Removes all items with Timestamp &lt; cutoff.
        /// Implementation: drain, filter, rebuild preserving original LIFO.
        /// Complexity: O(n).
        /// </summary>
        public void RemoveOlderThan(DateTime cutoff)
        {
            lock (_lock)
            {
                var keep = new List<TemporalItem<T>>();

                // Drain newest->oldest
                while (_stack.TryPop(out var item))
                    if (item.Timestamp >= cutoff) keep.Add(item);

                // Rebuild pushing oldest->newest to restore original LIFO
                for (int i = keep.Count - 1; i >= 0; i--)
                    _stack.Push(keep[i]);
            }
        }

        /// <summary>
        /// Calculates the time span between the earliest and latest timestamps.
        /// Returns TimeSpan.Zero if fewer than two items.
        /// </summary>
        public TimeSpan GetTimeSpan()
        {
            lock (_lock)
            {
                if (_stack.IsEmpty) return TimeSpan.Zero;

                var snapshot = _stack.ToList();
                if (snapshot.Count < 2) return TimeSpan.Zero;

                var min = snapshot[0].Timestamp;
                var max = snapshot[0].Timestamp;
                for (int i = 1; i < snapshot.Count; i++)
                {
                    var ts = snapshot[i].Timestamp;
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
        public int CountInRange(DateTime from, DateTime to)
        {
            if (to < from) throw new ArgumentException("to must be >= from", nameof(to));
            lock (_lock) return _stack.Count(i => i.Timestamp >= from && i.Timestamp <= to);
        }

        /// <summary> 
        /// Removes all items. 
        /// </summary>
        public void Clear()
        {
            lock (_lock) _stack.Clear();
        }

        /// <summary>
        /// Removes all items with timestamps in [from, to] (inclusive).
        /// Implementation: drain, filter, rebuild preserving LIFO.
        /// </summary>
        public void RemoveRange(DateTime from, DateTime to)
        {
            if (to < from) throw new ArgumentException("to must be >= from", nameof(to));
            lock (_lock)
            {
                var keep = new List<TemporalItem<T>>();
                while (_stack.TryPop(out var item))
                    if (item.Timestamp < from || item.Timestamp > to)
                        keep.Add(item);

                for (int i = keep.Count - 1; i >= 0; i--)
                    _stack.Push(keep[i]);
            }
        }

        /// <summary>
        /// Gets the most recent item by timestamp, or null if empty. Complexity: O(n).
        /// </summary>
        public TemporalItem<T>? GetLatest()
        {
            lock (_lock)
            {
                if (_stack.IsEmpty) return null;
                TemporalItem<T>? best = null;
                DateTime bestTs = DateTime.MinValue;
                foreach (var it in _stack)
                    if (it.Timestamp > bestTs) { bestTs = it.Timestamp; best = it; }
                return best;
            }
        }

        /// <summary>
        /// Gets the earliest item by timestamp, or null if empty. Complexity: O(n).
        /// </summary>
        public TemporalItem<T>? GetEarliest()
        {
            lock (_lock)
            {
                if (_stack.IsEmpty) return null;
                TemporalItem<T>? best = null;
                DateTime bestTs = DateTime.MaxValue;
                foreach (var it in _stack)
                    if (it.Timestamp < bestTs) { bestTs = it.Timestamp; best = it; }
                return best;
            }
        }

        /// <summary>
        /// Gets all items strictly before <paramref name="time"/>, ordered by ascending timestamp.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetBefore(DateTime time)
        {
            lock (_lock)
            {
                return _stack
                    .Where(i => i.Timestamp < time)
                    .OrderBy(i => i.Timestamp)
                    .ToList();
            }
        }

        /// <summary>
        /// Gets all items strictly after <paramref name="time"/>, ordered by ascending timestamp.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetAfter(DateTime time)
        {
            lock (_lock)
            {
                return _stack
                    .Where(i => i.Timestamp > time)
                    .OrderBy(i => i.Timestamp)
                    .ToList();
            }
        }
    }
}