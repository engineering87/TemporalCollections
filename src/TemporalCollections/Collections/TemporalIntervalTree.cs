// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe interval tree for storing temporal intervals with associated values.
    /// Supports insertion, removal, querying of overlapping intervals and time-based operations via <see cref="ITimeQueryable{T}"/>.
    /// Public API uses DateTime; internal timeline is DateTimeOffset (UTC).
    /// Treap-balanced (randomized) BST over (Start, End, Value) with augmented MaxEnd for interval queries.
    /// </summary>
    /// <typeparam name="T">The type of the value associated with each interval.</typeparam>
    public class TemporalIntervalTree<T> : TimeQueryableBase<T>
    {
        /// <summary>
        /// Represents a single node in the interval tree (Treap node).
        /// </summary>
        private sealed class Node
        {
            /// <summary>
            /// Interval start (inclusive), UTC.
            /// </summary>
            public DateTimeOffset Start;

            /// <summary>
            /// Interval end (inclusive), UTC.
            /// </summary>
            public DateTimeOffset End;

            /// <summary>
            /// Associated value for the interval.
            /// </summary>
            public T Value = default!;

            /// <summary>
            /// The maximum End of this node or any of its descendants (UTC).
            /// </summary>
            public DateTimeOffset MaxEnd;

            /// <summary>
            /// Treap priority (min-heap): lower value means higher priority.
            /// </summary>
            public int Priority;

            public Node? Left;
            public Node? Right;

            public Node(DateTimeOffset start, DateTimeOffset end, T value, int priority)
            {
                if (end < start)
                    throw new ArgumentException("End must be >= Start", nameof(end));

                Start = start;
                End = end;
                Value = value;
                Priority = priority;
                MaxEnd = end;
            }
        }

        private Node? _root;
        private readonly Lock _lock = new();
        private readonly Random _rng = new(); // used only under _lock

        /// <summary>
        /// Inserts a new interval with an associated value into the tree.
        /// </summary>
        public void Insert(DateTimeOffset start, DateTimeOffset end, T value)
        {
            // Normalize to UTC offsets
            var s = start;
            var e = end;
            if (e < s) throw new ArgumentException("end must be >= start", nameof(end));

            lock (_lock)
            {
                _root = InsertTreap(_root, s, e, value);
            }
        }

        /// <summary>
        /// Removes an interval with the exact same start, end, and value from the tree.
        /// </summary>
        public bool Remove(DateTimeOffset start, DateTimeOffset end, T value)
        {
            var s = start;
            var e = end;

            lock (_lock)
            {
                bool removed;
                (_root, removed) = RemoveTreap(_root, s, e, value);
                return removed;
            }
        }

        /// <summary>
        /// Returns all values whose intervals overlap with the given query range.
        /// The returned items are wrapped as <see cref="TemporalItem{T}"/> where Timestamp equals interval Start.
        /// </summary>
        public override IEnumerable<TemporalItem<T>> GetInRange(DateTimeOffset queryStart, DateTimeOffset queryEnd)
        {
            if (queryStart > queryEnd) throw new ArgumentException("'queryStart' must be <= 'queryEnd'.");

            var qs = queryStart;
            var qe = queryEnd;
            if (qs > qe) (qs, qe) = (qe, qs);

            lock (_lock)
            {
                var result = new List<TemporalItem<T>>();
                QueryCollect(_root, qs, qe, result);
                return result;
            }
        }

        /// <summary>
        /// Removes all intervals that have already ended strictly before the cutoff (End &lt; cutoff).
        /// </summary>
        public override void RemoveOlderThan(DateTimeOffset cutoff)
        {
            var c = cutoff;

            lock (_lock)
            {
                _root = RemoveOlderThanInternal(_root, c);
            }
        }

        /// <summary>
        /// Returns all values whose intervals overlap with the given query range (values only).
        /// </summary>
        public IList<T> Query(DateTimeOffset queryStart, DateTimeOffset queryEnd)
        {
            if (queryStart > queryEnd)
                throw new ArgumentException("'queryStart' must be <= 'queryEnd'.");

            var qs = queryStart;
            var qe = queryEnd;
            if (qs > qe) (qs, qe) = (qe, qs);

            lock (_lock)
            {
                var result = new List<T>();
                QueryValues(_root, qs, qe, result);
                return result;
            }
        }

        /// <summary>
        /// Returns the total timespan covered by items in the collection
        /// as (latest.Start - earliest.Start). Returns TimeSpan.Zero if empty or single item.
        /// </summary>
        public override TimeSpan GetTimeSpan()
        {
            lock (_lock)
            {
                if (_root is null) return TimeSpan.Zero;

                var minNode = FindMinByStart(_root);
                var maxNode = FindMaxByStart(_root);
                if (minNode is null || maxNode is null) return TimeSpan.Zero;

                var span = maxNode.Start - minNode.Start; // DateTimeOffset subtraction -> TimeSpan
                return span < TimeSpan.Zero ? TimeSpan.Zero : span;
            }
        }

        /// <summary>
        /// Returns the number of items whose timestamps fall within [from, to] (inclusive). Timestamp == interval Start.
        /// </summary>
        public override int CountInRange(DateTimeOffset from, DateTimeOffset to)
        {
            var f = from;
            var t = to;
            if (f > t) (f, t) = (t, f);

            lock (_lock)
            {
                return CountByStartRange(_root, f, t);
            }
        }

        /// <summary>
        /// Removes all items whose timestamps (Start) fall within [from, to] (inclusive).
        /// </summary>
        public override void RemoveRange(DateTimeOffset from, DateTimeOffset to)
        {
            var f = from;
            var t = to;
            if (f > t) (f, t) = (t, f);

            lock (_lock)
            {
                _root = RemoveByStartRange(_root, f, t);
            }
        }

        /// <summary>
        /// Removes all items from the collection.
        /// </summary>
        public override void Clear()
        {
            lock (_lock)
            {
                _root = null;
            }
        }

        /// <summary>
        /// Retrieves the latest item by timestamp (max Start) or null if empty.
        /// </summary>
        public override TemporalItem<T>? GetLatest()
        {
            lock (_lock)
            {
                if (_root is null) return null;
                var n = FindMaxByStart(_root);
                return n is null ? null : new TemporalItem<T>(n.Value, n.Start);
            }
        }

        /// <summary>
        /// Retrieves the earliest item by timestamp (min Start) or null if empty.
        /// </summary>
        public override TemporalItem<T>? GetEarliest()
        {
            lock (_lock)
            {
                if (_root is null) return null;
                var n = FindMinByStart(_root);
                return n is null ? null : new TemporalItem<T>(n.Value, n.Start);
            }
        }

        /// <summary>
        /// Retrieves all items with timestamp strictly before the specified time (Start &lt; time).
        /// </summary>
        public override IEnumerable<TemporalItem<T>> GetBefore(DateTimeOffset time)
        {
            var cutoff = time;

            lock (_lock)
            {
                var list = new List<TemporalItem<T>>();
                CollectBefore(_root, cutoff, list);
                return list;
            }
        }

        /// <summary>
        /// Retrieves all items with timestamp strictly after the specified time (Start &gt; time).
        /// </summary>
        public override IEnumerable<TemporalItem<T>> GetAfter(DateTimeOffset time)
        {
            var cutoff = time;

            lock (_lock)
            {
                var list = new List<TemporalItem<T>>();
                CollectAfter(_root, cutoff, list);
                return list;
            }
        }

        /// <summary>
        /// Counts the number of items with timestamp (Start) greater than or equal to the specified cutoff.
        /// </summary>
        public override int CountSince(DateTimeOffset since)
        {
            var s = since;
            lock (_lock)
            {
                return CountByStartGte(_root, s);
            }
        }

        /// <summary>
        /// Returns the interval whose start time is closest to <paramref name="time"/> (using Start as timestamp).
        /// If the tree is empty, returns <c>null</c>.
        /// In case of a tie (same distance before/after), the later interval (Start ≥ time) is returned.
        /// Complexity: O(h).
        /// </summary>
        public override TemporalItem<T>? GetNearest(DateTimeOffset time)
        {
            var target = time;

            lock (_lock)
            {
                if (_root is null) return null;

                var floor = FindFloorByStart(_root, target); // greatest Start <= target
                var ceil = FindCeilByStart(_root, target);  // smallest Start >= target

                if (floor is null) return ceil is null ? null : new TemporalItem<T>(ceil.Value, ceil.Start);
                if (ceil is null) return new TemporalItem<T>(floor.Value, floor.Start);

                long beforeDiff = (long)(target.UtcTicks - floor.Start.UtcTicks); // >= 0
                long afterDiff = (long)(ceil.Start.UtcTicks - target.UtcTicks);  // >= 0

                // Tie-break: prefer the later one (ceil)
                return (afterDiff <= beforeDiff)
                    ? new TemporalItem<T>(ceil.Value, ceil.Start)
                    : new TemporalItem<T>(floor.Value, floor.Start);
            }
        }

        #region Internal helpers (UTC DateTimeOffset, Treap)

        /// <summary>
        /// Total order over (Start, End, Value) to avoid long equal-key chains.
        /// </summary>
        private static int CompareKey(DateTimeOffset s1, DateTimeOffset e1, T v1,
                                      DateTimeOffset s2, DateTimeOffset e2, T v2)
        {
            int c = s1.CompareTo(s2);
            if (c != 0) return c;
            c = e1.CompareTo(e2);
            if (c != 0) return c;

            if (EqualityComparer<T>.Default.Equals(v1, v2)) return 0;

            int h1 = HashCode.Combine(v1);
            int h2 = HashCode.Combine(v2);
            return h1 < h2 ? -1 : (h1 > h2 ? 1 : 0);
        }

        /// <summary>
        /// Recomputes MaxEnd from children and own End.
        /// </summary>
        private static void Update(Node n)
        {
            var max = n.End;
            if (n.Left is not null && n.Left.MaxEnd > max) max = n.Left.MaxEnd;
            if (n.Right is not null && n.Right.MaxEnd > max) max = n.Right.MaxEnd;
            n.MaxEnd = max;
        }

        private static Node RotateRight(Node y)
        {
            var x = y.Left!;
            var t2 = x.Right;

            x.Right = y;
            y.Left = t2;

            Update(y);
            Update(x);
            return x;
        }

        private static Node RotateLeft(Node x)
        {
            var y = x.Right!;
            var t2 = y.Left;

            y.Left = x;
            x.Right = t2;

            Update(x);
            Update(y);
            return y;
        }

        /// <summary>
        /// Treap insert with priority heap property (min-heap).
        /// </summary>
        private Node InsertTreap(Node? node, DateTimeOffset start, DateTimeOffset end, T value)
        {
            if (node is null)
                return new Node(start, end, value, _rng.Next());

            int cmp = CompareKey(start, end, value, node.Start, node.End, node.Value);

            if (cmp < 0)
            {
                node.Left = InsertTreap(node.Left, start, end, value);
                if (node.Left!.Priority < node.Priority)
                    node = RotateRight(node);
            }
            else if (cmp > 0)
            {
                node.Right = InsertTreap(node.Right, start, end, value);
                if (node.Right!.Priority < node.Priority)
                    node = RotateLeft(node);
            }
            else
            {
                // Exact duplicate (Start, End, Value): no-op.
                return node;
            }

            Update(node);
            return node;
        }

        /// <summary>
        /// Removes a node matching (start, end, value) using treap deletion:
        /// rotate the node down according to child priorities until it becomes a leaf, then drop it.
        /// </summary>
        private static (Node? node, bool removed) RemoveTreap(Node? node, DateTimeOffset start, DateTimeOffset end, T value)
        {
            if (node is null) return (null, false);

            int cmp = CompareKey(start, end, value, node.Start, node.End, node.Value);

            bool removed;
            if (cmp < 0)
            {
                (node.Left, removed) = RemoveTreap(node.Left, start, end, value);
            }
            else if (cmp > 0)
            {
                (node.Right, removed) = RemoveTreap(node.Right, start, end, value);
            }
            else
            {
                // Found target node: delete by rotating down.
                removed = true;
                node = DeleteRoot(node);
                return (node, true);
            }

            if (node is not null) Update(node);
            return (node, removed);
        }

        /// <summary>
        /// Deletes the root of a treap subtree by rotating it down until it has at most one child, then removing it.
        /// </summary>
        private static Node? DeleteRoot(Node node)
        {
            if (node.Left is null && node.Right is null)
                return null;

            if (node.Left is null)
                return node.Right;

            if (node.Right is null)
                return node.Left;

            // Both children exist: rotate towards the child with smaller priority (higher heap priority).
            if (node.Left.Priority < node.Right.Priority)
            {
                node = RotateRight(node);
                node.Right = DeleteRoot(node.Right!);
            }
            else
            {
                node = RotateLeft(node);
                node.Left = DeleteRoot(node.Left!);
            }

            Update(node);
            return node;
        }

        /// <summary>
        /// Counts nodes with Start in [from, to] using BST pruning.
        /// </summary>
        private static int CountByStartRange(Node? node, DateTimeOffset from, DateTimeOffset to)
        {
            if (node is null) return 0;

            if (node.Start < from)
                return CountByStartRange(node.Right, from, to);

            if (node.Start > to)
                return CountByStartRange(node.Left, from, to);

            // node.Start within [from, to]
            return 1 + CountByStartRange(node.Left, from, to) + CountByStartRange(node.Right, from, to);
        }

        /// <summary>
        /// Removes nodes whose End < cutoff and returns new subtree root (treap-preserving).
        /// </summary>
        private static Node? RemoveOlderThanInternal(Node? node, DateTimeOffset cutoff)
        {
            if (node is null) return null;

            node.Left = RemoveOlderThanInternal(node.Left, cutoff);
            node.Right = RemoveOlderThanInternal(node.Right, cutoff);

            if (node.End < cutoff)
            {
                node = DeleteRoot(node);
                // Ensure that possible replacement at this position is also checked
                node = RemoveOlderThanInternal(node, cutoff);
                return node;
            }

            Update(node);
            return node;
        }

        /// <summary>
        /// Removes nodes whose Start is in [from, to] (inclusive) and returns new subtree root (treap-preserving).
        /// </summary>
        private static Node? RemoveByStartRange(Node? node, DateTimeOffset from, DateTimeOffset to)
        {
            if (node is null) return null;

            node.Left = RemoveByStartRange(node.Left, from, to);
            node.Right = RemoveByStartRange(node.Right, from, to);

            if (node.Start >= from && node.Start <= to)
            {
                node = DeleteRoot(node);
                node = RemoveByStartRange(node, from, to);
                return node;
            }

            Update(node);
            return node;
        }

        /// <summary>
        /// Collect TemporalItem<T> for intervals overlapping [qs, qe].
        /// </summary>
        private static void QueryCollect(Node? node, DateTimeOffset qs, DateTimeOffset qe, List<TemporalItem<T>> result)
        {
            if (node is null) return;

            // Left subtree can overlap only if its MaxEnd >= qs
            if (node.Left is not null && node.Left.MaxEnd >= qs)
                QueryCollect(node.Left, qs, qe, result);

            // Current node overlaps if Start <= qe && End >= qs
            if (node.Start <= qe && node.End >= qs)
                result.Add(new TemporalItem<T>(node.Value, node.Start));

            // Right subtree may have starts <= qe
            if (node.Right is not null && node.Start <= qe)
                QueryCollect(node.Right, qs, qe, result);
        }

        /// <summary>
        /// Collect values for compatibility with "Query" method.
        /// </summary>
        private static void QueryValues(Node? node, DateTimeOffset qs, DateTimeOffset qe, List<T> result)
        {
            if (node is null) return;

            if (node.Left is not null && node.Left.MaxEnd >= qs)
                QueryValues(node.Left, qs, qe, result);

            if (node.Start <= qe && node.End >= qs)
                result.Add(node.Value);

            if (node.Right is not null && node.Start <= qe)
                QueryValues(node.Right, qs, qe, result);
        }

        /// <summary>
        /// Returns leftmost by Start.
        /// </summary>
        private static Node? FindMinByStart(Node? node)
        {
            if (node is null) return null;
            while (node.Left is not null) node = node.Left;
            return node;
        }

        /// <summary>
        /// Returns rightmost by Start.
        /// </summary>
        private static Node? FindMaxByStart(Node? node)
        {
            if (node is null) return null;
            while (node.Right is not null) node = node.Right;
            return node;
        }

        /// <summary>
        /// In-order traversal collecting nodes with Start &lt; time; prunes when possible.
        /// </summary>
        private static void CollectBefore(Node? node, DateTimeOffset time, List<TemporalItem<T>> acc)
        {
            if (node is null) return;

            if (node.Start >= time)
            {
                CollectBefore(node.Left, time, acc);
                return;
            }

            // node.Start < time
            CollectBefore(node.Left, time, acc);
            acc.Add(new TemporalItem<T>(node.Value, node.Start));
            CollectBefore(node.Right, time, acc);
        }

        /// <summary>
        /// In-order traversal collecting nodes with Start &gt; time; prunes when possible.
        /// </summary>
        private static void CollectAfter(Node? node, DateTimeOffset time, List<TemporalItem<T>> acc)
        {
            if (node is null) return;

            if (node.Start <= time)
            {
                CollectAfter(node.Right, time, acc);
                return;
            }

            // node.Start > time
            CollectAfter(node.Left, time, acc);
            acc.Add(new TemporalItem<T>(node.Value, node.Start));
            CollectAfter(node.Right, time, acc);
        }

        private static int CountWithEndAtOrAfter(Node? node, DateTimeOffset cutoff)
        {
            if (node is null) return 0;

            int count = 0;

            if (node.Left is not null && node.Left.MaxEnd >= cutoff)
                count += CountWithEndAtOrAfter(node.Left, cutoff);

            if (node.End >= cutoff)
                count++;

            if (node.Right is not null && node.Right.MaxEnd >= cutoff)
                count += CountWithEndAtOrAfter(node.Right, cutoff);

            return count;
        }

        /// <summary>
        /// Finds the node with the greatest Start <= <paramref name="target"/>; returns <c>null</c> if none.
        /// </summary>
        private static Node? FindFloorByStart(Node? node, DateTimeOffset target)
        {
            Node? res = null;
            while (node is not null)
            {
                if (node.Start > target)
                {
                    node = node.Left;
                }
                else
                {
                    res = node;          // candidate floor
                    node = node.Right;   // try to get closer (greater Start but still <= target)
                }
            }
            return res;
        }

        /// <summary>
        /// Finds the node with the smallest Start >= <paramref name="target"/>; returns <c>null</c> if none.
        /// </summary>
        private static Node? FindCeilByStart(Node? node, DateTimeOffset target)
        {
            Node? res = null;
            while (node is not null)
            {
                if (node.Start < target)
                {
                    node = node.Right;
                }
                else
                {
                    res = node;         // candidate ceil
                    node = node.Left;   // try to get closer (smaller Start but still >= target)
                }
            }
            return res;
        }

        /// <summary>
        /// Counts nodes whose Start is greater than or equal to <paramref name="k"/>.
        /// This leverages the BST ordering by Start. Without subtree sizes,
        /// the complexity is O(h + visited), which is acceptable for a treap
        /// and keeps the implementation simple.
        /// </summary>
        /// <param name="node">Current subtree root.</param>
        /// <param name="k">Start cutoff (inclusive).</param>
        /// <returns>Count of nodes with Start >= <paramref name="k"/>.</returns>
        private static int CountByStartGte(Node? node, DateTimeOffset k)
        {
            if (node is null) return 0;

            if (node.Start < k)
            {
                // All nodes in the left subtree have Start < node.Start < k → skip them.
                return CountByStartGte(node.Right, k);
            }

            // node.Start >= k → count this node and continue on both sides.
            // (Without subtree sizes we must explore the right subtree.)
            return 1 + CountByStartGte(node.Left, k) + CountByStartGte(node.Right, k);
        }

        #endregion
    }
}