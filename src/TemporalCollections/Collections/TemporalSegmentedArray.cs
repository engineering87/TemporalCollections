// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe, time-ordered segmented array optimized for append-in-order workloads and
    /// periodic retention (chunk dropping). Segments keep items sorted by UTC ticks; queries
    /// use binary search on segment boundaries and within segments.
    ///
    /// Design goals
    /// ------------
    /// • Append amortized O(1) when timestamps are non-decreasing.
    /// • Range queries O(log S + K) where S = number of segments and K = returned items.
    /// • Efficient retention: <see cref="RemoveOlderThan(System.DateTimeOffset)"/> can drop whole segments.
    /// </summary>
    public sealed class TemporalSegmentedArray<T> : TimeQueryableBase<T>
    {
        /// <summary>
        /// Internal segment of contiguous, timestamp-ordered items.
        /// </summary>
        private sealed class Segment
        {
            public TemporalItem<T>[] Items;
            public int Count;
            public long MinTicks;
            public long MaxTicks;

            /// <summary>
            /// Initializes a new segment with the specified capacity.
            /// </summary>
            /// <param name="capacity">Maximum number of items in this segment.</param>
            public Segment(int capacity)
            {
                Items = new TemporalItem<T>[capacity];
                Count = 0;
                MinTicks = long.MaxValue;
                MaxTicks = long.MinValue;
            }

            /// <summary>
            /// Indicates whether the segment is full.
            /// </summary>
            public bool IsFull => Count == Items.Length;

            /// <summary>
            /// Appends an item to the end of this segment (caller guarantees ordering).
            /// </summary>
            /// <param name="item">Item to append.</param>
            public void Append(in TemporalItem<T> item)
            {
                Items[Count++] = item;
                long t = item.Timestamp.UtcTicks;
                if (t < MinTicks) MinTicks = t;
                if (t > MaxTicks) MaxTicks = t;
            }

            /// <summary>
            /// Inserts an item at the specified index, shifting existing items to the right.
            /// </summary>
            /// <param name="index">Zero-based insertion index.</param>
            /// <param name="item">Item to insert.</param>
            public void InsertAt(int index, in TemporalItem<T> item)
            {
                if (index < Count) Array.Copy(Items, index, Items, index + 1, Count - index);
                Items[index] = item;
                Count++;
                long t = item.Timestamp.UtcTicks;
                if (t < MinTicks) MinTicks = t;
                if (t > MaxTicks) MaxTicks = t;
                else if (index == 0) MinTicks = Items[0].Timestamp.UtcTicks;
                else if (index == Count - 1) MaxTicks = Items[Count - 1].Timestamp.UtcTicks;
            }

            /// <summary>
            /// Removes a contiguous range from this segment.
            /// </summary>
            /// <param name="start">Start index (inclusive).</param>
            /// <param name="removeCount">Number of items to remove.</param>
            public void RemoveRangeIndices(int start, int removeCount)
            {
                if (removeCount <= 0) return;
                int tail = Count - (start + removeCount);
                if (tail > 0) Array.Copy(Items, start + removeCount, Items, start, tail);
                Count -= removeCount;
                if (Count == 0)
                {
                    MinTicks = long.MaxValue;
                    MaxTicks = long.MinValue;
                }
                else
                {
                    MinTicks = Items[0].Timestamp.UtcTicks;
                    MaxTicks = Items[Count - 1].Timestamp.UtcTicks;
                }
            }

            /// <summary>
            /// Binary search: first index with timestamp ticks &gt;= <paramref name="ticks"/>.
            /// </summary>
            /// <param name="ticks">UTC ticks to search for.</param>
            /// <returns>Index of the lower bound within this segment.</returns>
            public int LowerBound(long ticks)
            {
                int lo = 0, hi = Count;
                while (lo < hi)
                {
                    int mid = lo + ((hi - lo) >> 1);
                    long m = Items[mid].Timestamp.UtcTicks;
                    if (m < ticks) lo = mid + 1; else hi = mid;
                }
                return lo;
            }

            /// <summary>
            /// Binary search: first index with timestamp ticks &gt; <paramref name="ticks"/>.
            /// </summary>
            /// <param name="ticks">UTC ticks to search for.</param>
            /// <returns>Index of the upper exclusive bound within this segment.</returns>
            public int UpperExclusive(long ticks)
            {
                int lo = 0, hi = Count;
                while (lo < hi)
                {
                    int mid = lo + ((hi - lo) >> 1);
                    long m = Items[mid].Timestamp.UtcTicks;
                    if (m <= ticks) lo = mid + 1; else hi = mid;
                }
                return lo;
            }
        }

        private readonly List<Segment> _segments = [];
        private int _count;
        private readonly int _segmentCapacity;
        private readonly Lock _lock = new();

        private const int DefaultSegmentCapacity = 1024;

        /// <summary>
        /// Initializes a new <see cref="TemporalSegmentedArray{T}"/> with a given segment capacity.
        /// </summary>
        /// <param name="segmentCapacity">Maximum number of items per segment; must be &gt; 0.</param>
        public TemporalSegmentedArray(int segmentCapacity = DefaultSegmentCapacity)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(segmentCapacity);

            _segmentCapacity = segmentCapacity;
        }

        /// <summary>
        /// Gets the total number of items currently stored.
        /// </summary>
        public int Count 
        { 
            get 
            { 
                lock(_lock) 
                { 
                    return _count; 
                } 
            } 
        }

        /// <summary>
        /// Gets the current number of segments.
        /// </summary>
        public int SegmentCount 
        { 
            get 
            {
                lock (_lock)
                { 
                    return _segments.Count; 
                } 
            } 
        }

        /// <summary>
        /// Returns an enumerator over a stable snapshot in chronological order.
        /// Enumeration never holds locks.
        /// </summary>
        public IEnumerator<TemporalItem<T>> GetEnumerator()
        {
            var snap = ToArray();
            for (int i = 0; i < snap.Length; i++) 
                yield return snap[i];
        }

        #region Mutation API

        /// <summary>
        /// Adds a temporal item, keeping the overall order by timestamp.
        /// Fast-path appends to the last segment if timestamps are non-decreasing;
        /// otherwise inserts positionally (and may split a full segment).
        /// </summary>
        /// <param name="item">Item to add.</param>
        public void Add(TemporalItem<T> item)
        {
            lock (_lock)
            {
                if (_segments.Count == 0)
                {
                    var s = new Segment(_segmentCapacity);
                    s.Append(item);
                    _segments.Add(s);
                    _count = 1;
                    return;
                }

                var last = _segments[^1];
                long t = item.Timestamp.UtcTicks;
                if (last.Count < _segmentCapacity && (last.Count == 0 || last.MaxTicks <= t))
                {
                    last.Append(item);
                    _count++;
                    return;
                }

                var (segIdx, elemIdx) = LowerBoundGlobal(t);
                InsertAt(segIdx, elemIdx, item);
                _count++;
            }
        }

        /// <summary>
        /// Creates a <see cref="TemporalItem{T}"/> with a monotonic UTC timestamp and adds it.
        /// </summary>
        /// <param name="value">Value to wrap.</param>
        /// <returns>The created <see cref="TemporalItem{T}"/>.</returns>
        public TemporalItem<T> AddValue(T value)
        {
            var it = TemporalItem<T>.Create(value);
            Add(it);
            return it;
        }

        /// <summary>
        /// Adds a sequence of items, inserting each in sorted position.
        /// If the input is globally sorted, prefer <see cref="AddSorted(IEnumerable{TemporalItem{T}})"/>.
        /// </summary>
        /// <param name="items">Items to add.</param>
        public void AddRange(IEnumerable<TemporalItem<T>> items)
        {
            foreach (var it in items) 
                Add(it);
        }

        /// <summary>
        /// Adds items known to be globally sorted by timestamp ascending.
        /// Optimizes the append path; falls back to positional insert if ordering is violated.
        /// </summary>
        /// <param name="sortedItems">Sorted items to add.</param>
        public void AddSorted(IEnumerable<TemporalItem<T>> sortedItems)
        {
            lock (_lock)
            {
                foreach (var it in sortedItems)
                {
                    if (_segments.Count == 0)
                    {
                        var s = new Segment(_segmentCapacity);
                        s.Append(it);
                        _segments.Add(s);
                        _count = 1;
                        continue;
                    }
                    var last = _segments[^1];
                    long t = it.Timestamp.UtcTicks;
                    if (last.Count < _segmentCapacity && (last.Count == 0 || last.MaxTicks <= t))
                    {
                        last.Append(it);
                        _count++;
                    }
                    else
                    {
                        var (segIdx, elemIdx) = LowerBoundGlobal(t);
                        InsertAt(segIdx, elemIdx, it);
                        _count++;
                    }
                }
            }
        }
        #endregion

        #region TimeQueryableBase<T> overrides

        /// <inheritdoc />
        public override IEnumerable<TemporalItem<T>> GetInRange(DateTimeOffset from, DateTimeOffset to)
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return [];
                long f = from.UtcTicks, t = to.UtcTicks; 
                if (f > t) 
                    (f, t) = (t, f);
                var list = new List<TemporalItem<T>>();
                int startSeg = FirstSegWithMaxGte(f);
                if (startSeg < 0) 
                    return [];
                for (int s = startSeg; s < _segments.Count; s++)
                {
                    var seg = _segments[s];
                    if (seg.MinTicks > t) 
                        break;
                    int i0 = seg.LowerBound(f);
                    int i1 = seg.UpperExclusive(t);
                    for (int i = i0; i < i1; i++) 
                        list.Add(seg.Items[i]);
                }
                return list.Count == 0 ? [] : list.ToArray();
            }
        }

        /// <inheritdoc />
        public override void RemoveOlderThan(DateTimeOffset cutoff)
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return;
                long c = cutoff.UtcTicks;

                // Drop whole segments strictly before cutoff
                int drop = 0;
                while (drop < _segments.Count && _segments[drop].MaxTicks < c) drop++;
                if (drop > 0) _segments.RemoveRange(0, drop);

                if (_segments.Count == 0) 
                { 
                    _count = 0; 
                    return; 
                }

                // Trim partially the first remaining segment
                var first = _segments[0];
                if (first.MinTicks < c && first.MaxTicks >= c)
                {
                    int cut = first.LowerBound(c);
                    first.RemoveRangeIndices(0, cut);
                }

                // Recompute count
                _count = 0;
                foreach (var seg in _segments) 
                    _count += seg.Count;
            }
        }

        /// <inheritdoc />
        public override int CountInRange(DateTimeOffset from, DateTimeOffset to)
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return 0;
                long f = from.UtcTicks, t = to.UtcTicks; 
                if (f > t) 
                    (f, t) = (t, f);
                int total = 0;
                int startSeg = FirstSegWithMaxGte(f);
                if (startSeg < 0) 
                    return 0;
                for (int s = startSeg; s < _segments.Count; s++)
                {
                    var seg = _segments[s];
                    if (seg.MinTicks > t) break;
                    int i0 = seg.LowerBound(f);
                    int i1 = seg.UpperExclusive(t);
                    total += Math.Max(0, i1 - i0);
                }
                return total;
            }
        }

        /// <inheritdoc />
        public override void RemoveRange(DateTimeOffset from, DateTimeOffset to)
        {
            lock (_lock)
            {
                if (_count == 0) return;
                long f = from.UtcTicks, t = to.UtcTicks; 
                if (f > t) 
                    (f, t) = (t, f);

                int firstHit = FirstSegWithMaxGte(f);
                if (firstHit < 0) 
                    return;

                for (int s = firstHit; s < _segments.Count; s++)
                {
                    var seg = _segments[s];
                    if (seg.MinTicks > t) 
                        break;

                    if (f <= seg.MinTicks && seg.MaxTicks <= t)
                    {
                        _segments.RemoveAt(s); 
                        s--; 
                        continue;
                    }

                    int i0 = seg.LowerBound(f);
                    int i1 = seg.UpperExclusive(t);
                    seg.RemoveRangeIndices(i0, i1 - i0);
                    if (seg.Count == 0) 
                    { 
                        _segments.RemoveAt(s); 
                        s--; 
                    }
                }

                // Recompute count
                _count = 0;
                foreach (var seg in _segments) 
                    _count += seg.Count;
            }
        }

        /// <inheritdoc />
        public override IEnumerable<TemporalItem<T>> GetBefore(DateTimeOffset time)
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return [];
                long c = time.UtcTicks;
                var list = new List<TemporalItem<T>>();
                foreach (var seg in _segments)
                {
                    if (seg.MinTicks >= c) 
                        break;
                    int end = seg.LowerBound(c);
                    for (int i = 0; i < end; i++) 
                        list.Add(seg.Items[i]);
                }
                return list.Count == 0 ? [] : list.ToArray();
            }
        }

        /// <inheritdoc />
        public override IEnumerable<TemporalItem<T>> GetAfter(DateTimeOffset time)
        {
            lock (_lock)
            {
                if (_count == 0)
                    return [];
                long c = time.UtcTicks;
                var list = new List<TemporalItem<T>>();
                int sIdx = FirstSegWithMaxGte(c + 1);
                if (sIdx < 0) 
                    return [];
                for (int s = sIdx; s < _segments.Count; s++)
                {
                    var seg = _segments[s];
                    int start = seg.UpperExclusive(c);
                    for (int i = start; i < seg.Count; i++) 
                        list.Add(seg.Items[i]);
                }
                return list.Count == 0 ? [] : list.ToArray();
            }
        }

        /// <inheritdoc />
        public override int CountSince(DateTimeOffset from)
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return 0;
                long f = from.UtcTicks;
                int total = 0;
                int sIdx = FirstSegWithMaxGte(f);
                if (sIdx < 0) 
                    return 0;
                for (int s = sIdx; s < _segments.Count; s++)
                {
                    var seg = _segments[s];
                    int start = seg.LowerBound(f);
                    total += seg.Count - start;
                }
                return total;
            }
        }

        /// <inheritdoc />
        public override TemporalItem<T>? GetNearest(DateTimeOffset time)
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return null;
                long x = time.UtcTicks;
                int sIdx = FirstSegWithMaxGte(x);
                if (sIdx < 0) 
                    return _segments[^1].Items[_segments[^1].Count - 1];
                var seg = _segments[sIdx];
                int idx = seg.LowerBound(x);
                TemporalItem<T>? candAfter = idx < seg.Count ? seg.Items[idx] : null;
                TemporalItem<T>? candBefore = null;
                if (idx > 0) candBefore = seg.Items[idx - 1];
                else if (sIdx > 0)
                {
                    var prevSeg = _segments[sIdx - 1];
                    if (prevSeg.Count > 0) 
                        candBefore = prevSeg.Items[prevSeg.Count - 1];
                }
                if (candBefore is null) 
                    return candAfter;
                if (candAfter is null) 
                    return candBefore;
                long dPrev = Math.Abs(x - candBefore.Timestamp.UtcTicks);
                long dNext = Math.Abs(candAfter.Timestamp.UtcTicks - x);
                return (dPrev <= dNext) ? candBefore : candAfter;
            }
        }

        /// <inheritdoc />
        public override TimeSpan GetTimeSpan()
        {
            lock (_lock)
            {
                if (_count <= 1) 
                    return TimeSpan.Zero;
                var first = _segments[0];
                var last = _segments[^1];
                return last.Items[last.Count - 1].Timestamp - first.Items[0].Timestamp;
            }
        }

        /// <inheritdoc />
        public override TemporalItem<T>? GetLatest()
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return null;
                var last = _segments[^1];
                return last.Count == 0 ? null : last.Items[last.Count - 1];
            }
        }

        /// <inheritdoc />
        public override TemporalItem<T>? GetEarliest()
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return null;
                var first = _segments[0];
                return first.Count == 0 ? null : first.Items[0];
            }
        }

        /// <inheritdoc />
        public override void Clear()
        {
            lock (_lock)
            {
                _segments.Clear();
                _count = 0;
            }
        }
        #endregion

        #region Utilities

        /// <summary>
        /// Materializes a compact array snapshot of the current contents in chronological order.
        /// </summary>
        /// <returns>An array containing all temporal items.</returns>
        public TemporalItem<T>[] ToArray()
        {
            lock (_lock)
            {
                if (_count == 0) 
                    return [];
                var arr = new TemporalItem<T>[_count];
                int pos = 0;
                foreach (var seg in _segments)
                {
                    if (seg.Count == 0) 
                        continue;
                    Array.Copy(seg.Items, 0, arr, pos, seg.Count);
                    pos += seg.Count;
                }
                return arr;
            }
        }

        /// <summary>
        /// Shrinks internal arrays to fit their actual counts (useful after heavy purges).
        /// </summary>
        public void TrimExcess()
        {
            lock (_lock)
            {
                foreach (var seg in _segments)
                {
                    if (seg.Count == seg.Items.Length) 
                        continue;
                    Array.Resize(ref seg.Items, seg.Count);
                }
                _segments.TrimExcess();
            }
        }
        #endregion

        #region Private helpers

        /// <summary>
        /// Finds the first segment whose <c>MaxTicks</c> is greater than or equal to <paramref name="ticks"/>.
        /// </summary>
        /// <param name="ticks">Target UTC ticks.</param>
        /// <returns>Segment index; -1 if no segment satisfies the condition.</returns>
        private int FirstSegWithMaxGte(long ticks)
        {
            int lo = 0, hi = _segments.Count - 1;
            if (hi < 0) 
                return -1;
            if (_segments[hi].MaxTicks < ticks) 
                return -1;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (_segments[mid].MaxTicks < ticks) 
                    lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        /// <summary>
        /// Global lower bound: first position whose timestamp ticks is greater than or equal to <paramref name="ticks"/>.
        /// </summary>
        /// <param name="ticks">UTC ticks to locate.</param>
        /// <returns>Tuple (segmentIndex, elementIndex) pointing to the insertion location.</returns>
        private (int segIdx, int elemIdx) LowerBoundGlobal(long ticks)
        {
            int s = FirstSegWithMaxGte(ticks);
            if (s < 0) 
                return (_segments.Count - 1, _segments[^1].Count);
            var seg = _segments[s];
            int i = seg.LowerBound(ticks);
            return (s, i);
        }

        /// <summary>
        /// Inserts an item at the global position; splits the segment when full.
        /// </summary>
        /// <param name="segIdx">Target segment index.</param>
        /// <param name="elemIdx">Insertion index within the segment.</param>
        /// <param name="item">Item to insert.</param>
        private void InsertAt(int segIdx, int elemIdx, in TemporalItem<T> item)
        {
            var seg = _segments[segIdx];
            if (!seg.IsFull)
            {
                seg.InsertAt(elemIdx, item);
                return;
            }

            // Split: move upper half to a new right segment
            int move = seg.Count / 2;
            var right = new Segment(Math.Max(_segmentCapacity, move));
            int startMove = seg.Count - move;
            Array.Copy(seg.Items, startMove, right.Items, 0, move);
            right.Count = move;
            right.MinTicks = right.Items[0].Timestamp.UtcTicks;
            right.MaxTicks = right.Items[move - 1].Timestamp.UtcTicks;

            // Shrink left segment
            seg.Count = startMove;
            seg.MinTicks = seg.Items[0].Timestamp.UtcTicks;
            seg.MaxTicks = seg.Items[seg.Count - 1].Timestamp.UtcTicks;

            // Link the new segment
            _segments.Insert(segIdx + 1, right);

            // Insert into the appropriate segment
            if (elemIdx <= seg.Count)
                seg.InsertAt(elemIdx, item);
            else
                right.InsertAt(elemIdx - seg.Count, item);
        }
        #endregion
    }
}