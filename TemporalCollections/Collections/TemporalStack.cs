// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe LIFO stack where each item has an insertion timestamp,
    /// enabling efficient time-based queries and cleanups.
    /// Note: <see cref="RemoveOlderThan"/> is an O(n) operation that rebuilds the stack
    /// and acquires an exclusive lock for its duration.
    /// </summary>
    /// <typeparam name="T">Type of the items stored in the stack.</typeparam>
    public class TemporalStack<T> : ITimeQueryable<T>
    {
        private readonly ConcurrentStack<TemporalItem<T>> _stack = new();
        private readonly Lock _sync = new();

        /// <summary>
        /// Gets the number of items currently in the stack.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_sync)
                {
                    return _stack.Count;
                }
            }
        }

        /// <summary>
        /// Pushes a new item onto the stack, recording the current timestamp.
        /// </summary>
        /// <param name="item">The item to push.</param>
        public void Push(T item)
        {
            var temporalItem = TemporalItem<T>.Create(item);
            lock (_sync)
            {
                _stack.Push(temporalItem);
            }
        }

        /// <summary>
        /// Pops the most recently pushed item from the stack.
        /// </summary>
        /// <returns>The popped item wrapped in a TemporalItem with its timestamp.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the stack is empty.</exception>
        public TemporalItem<T> Pop()
        {
            lock (_sync)
            {
                if (_stack.TryPop(out var item))
                    return item;
                throw new InvalidOperationException("Stack is empty.");
            }
        }

        /// <summary>
        /// Peeks at the most recently pushed item without removing it.
        /// </summary>
        /// <returns>The top item wrapped in a TemporalItem with its timestamp.</returns>
        /// <exception cref="InvalidOperationException">Thrown if the stack is empty.</exception>
        public TemporalItem<T> Peek()
        {
            lock (_sync)
            {
                if (_stack.TryPeek(out var item))
                    return item;
                throw new InvalidOperationException("Stack is empty.");
            }
        }

        /// <summary>
        /// Retrieves all items whose timestamps are within the inclusive range [from, to].
        /// The returned collection is a snapshot taken under lock to ensure consistency.
        /// </summary>
        /// <param name="from">Start of the time range (inclusive).</param>
        /// <param name="to">End of the time range (inclusive).</param>
        /// <returns>A snapshot list of temporal items within the specified time range.</returns>
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime from, DateTime to)
        {
            lock (_sync)
            {
                // materialize to a list while holding the lock to have a consistent snapshot
                return _stack.Where(i => i.Timestamp >= from && i.Timestamp <= to).ToList();
            }
        }

        /// <summary>
        /// Removes all items whose timestamps are strictly older than the specified cutoff (Timestamp &lt; cutoff).
        /// Implementation note: to remove arbitrary (older) elements from a stack we:
        /// 1) pop all elements,
        /// 2) keep only those newer-or-equal to the cutoff,
        /// 3) push them back preserving original LIFO order.
        /// This operation is O(n) and performed under an exclusive lock to avoid losing concurrent pushes.
        /// </summary>
        /// <param name="cutoff">The cutoff timestamp; items with Timestamp &lt; cutoff will be removed.</param>
        public void RemoveOlderThan(DateTime cutoff)
        {
            lock (_sync)
            {
                var keep = new List<TemporalItem<T>>();

                // Drain the stack
                while (_stack.TryPop(out var item))
                {
                    if (item.Timestamp >= cutoff)
                    {
                        // keep items that are newer-or-equal to cutoff
                        keep.Add(item);
                    }
                    // otherwise drop the old item
                }

                // push back the kept items so that original LIFO order is preserved
                // we popped newest->oldest, keep[] is newest->oldest; pushing from end -> start restores original stack
                for (int i = keep.Count - 1; i >= 0; i--)
                {
                    _stack.Push(keep[i]);
                }
            }
        }
    }
}