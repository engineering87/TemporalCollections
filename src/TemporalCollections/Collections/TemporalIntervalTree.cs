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
    /// <remarks>
    /// Time semantics of the <see cref="ITimeQueryable{T}"/> members:
    /// <list type="bullet">
    /// <item><see cref="GetInRange"/> returns intervals that <b>overlap</b> the range.</item>
    /// <item><see cref="RemoveOlderThan"/> removes intervals whose <b>End</b> is before the cutoff.</item>
    /// <item>All other members (CountInRange, RemoveRange, GetBefore, GetAfter, GetNearest,
    /// GetEarliest, GetLatest, GetTimeSpan, CountSince) use the interval <b>Start</b> as timestamp.</item>
    /// </list>
    /// Prefer the explicit <see cref="GetOverlapping"/>, <see cref="CountOverlapping"/> and
    /// <see cref="RemoveEndedBefore"/> for interval-specific queries.
    /// </remarks>
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
            /// Cached hash of <see cref="Value"/> (ordering key component).
            /// </summary>
            public readonly int Hash;

            /// <summary>
            /// Unique insertion sequence; final tie-break for distinct values sharing the same hash.
            /// </summary>
            public readonly long Seq;

            /// <summary>
            /// The maximum End of this node or any of its descendants (UTC).
            /// </summary>
            public DateTimeOffset MaxEnd;

            /// <summary>
            /// Treap priority (min-heap): lower value means higher priority.
            /// </summary>
            public readonly int Priority;

            public Node? Left;
            public Node? Right;

            public Node(DateTimeOffset start, DateTimeOffset end, T value, int hash, long seq, int priority)
            {
                if (end < start)
                    throw new ArgumentException("End must be >= Start", nameof(end));

                Start = start;
                End = end;
                Value = value;
                Hash = hash;
                Seq = seq;
                Priority = priority;
                MaxEnd = end;
            }
        }

        private Node? _root;
        private readonly Lock _lock = new();
        private readonly Random _rng = new(); // used only under _lock
        private long _seq; // used only under _lock

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
                int h = HashOf(value);

                // Exact duplicate (Start, End, Value): no-op.
                if (FindNode(_root, s, e, h, value) is not null)
                    return;

                _root = InsertTreap(_root, s, e, value, h, ++_seq);
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
                var target = FindNode(_root, s, e, HashOf(value), value);
                if (target is null)
                    return false;

                bool removed;
                (_root, removed) = RemoveTreap(_root, target.Start, target.End, target.Hash, target.Seq);
                return removed;
            }
        }

        /// <summary>
        /// Returns all values whose intervals overlap with the given query range.
        /// The returned items are wrapped as <see cref="TemporalItem{T}"/> where Timestamp equals interval Start.
        /// </summary>
        /// <remarks>
        /// Uses overlap semantics (Start &lt;= queryEnd and End &gt;= queryStart), unlike
        /// <see cref="CountInRange"/> which counts by Start. Equivalent to <see cref="GetOverlapping"/>.
        /// </remarks>
        public override IEnumerable<TemporalItem<T>> GetInRange(DateTimeOffset queryStart, DateTimeOffset queryEnd)
            => GetOverlapping(queryStart, queryEnd);

        /// <summary>
        /// Returns all intervals overlapping [queryStart, queryEnd] (inclusive), i.e.
        /// Start &lt;= queryEnd and End &gt;= queryStart. Timestamp of each item equals interval Start.
        /// </summary>
        public IReadOnlyList<TemporalItem<T>> GetOverlapping(DateTimeOffset queryStart, DateTimeOffset queryEnd)
        {
            var qs = queryStart;
            var qe = queryEnd;
            if (qs > qe) 
                (qs, qe) = (qe, qs);

            lock (_lock)
            {
                var result = new List<TemporalItem<T>>();
                QueryCollect(_root, qs, qe, result);
                return result;
            }
        }

        /// <summary>
        /// Returns the number of intervals overlapping [queryStart, queryEnd] (inclusive).
        /// Consistent with <see cref="GetOverlapping"/>.
        /// </summary>
        public int CountOverlapping(DateTimeOffset queryStart, DateTimeOffset queryEnd)
        {
            var qs = queryStart;
            var qe = queryEnd;
            if (qs > qe)
                (qs, qe) = (qe, qs);

            lock (_lock)
            {
                return CountOverlap(_root, qs, qe);
            }
        }

        /// <summary>
        /// Removes all intervals that ended strictly before <paramref name="cutoff"/> (End &lt; cutoff).
        /// Intervals still active at the cutoff are kept.
        /// </summary>
        public void RemoveEndedBefore(DateTimeOffset cutoff)
        {
            lock (_lock)
            {
                _root = RemoveOlderThanInternal(_root, cutoff);
            }
        }

        /// <summary>
        /// Removes all intervals that have already ended strictly before the cutoff (End &lt; cutoff).
        /// </summary>
        /// <remarks>
        /// Uses End semantics, unlike <see cref="RemoveRange"/> which removes by Start.
        /// Equivalent to <see cref="RemoveEndedBefore"/>.
        /// </remarks>
        public override void RemoveOlderThan(DateTimeOffset cutoff)
            => RemoveEndedBefore(cutoff);

        /// <summary>
        /// Returns all values whose intervals overlap with the given query range (values only).
        /// </summary>
        public IList<T> Query(DateTimeOffset queryStart, DateTimeOffset queryEnd)
        {
            var qs = queryStart;
            var qe = queryEnd;
            if (qs > qe)
                (qs, qe) = (qe, qs);

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
        /// <remarks>
        /// Counts by Start, so the result may differ from <c>GetInRange(from, to).Count()</c> (overlap semantics).
        /// Use <see cref="CountOverlapping"/> for an overlap-consistent count.
        /// </remarks>
        public override int CountInRange(DateTimeOffset from, DateTimeOffset to)
        {
            var f = from;
            var t = to;
            if (f > t)
                (f, t) = (t, f);

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
            if (f > t)
                (f, t) = (t, f);

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

        private static int HashOf(T value)
            => value is null ? 0 : EqualityComparer<T>.Default.GetHashCode(value);

        /// <summary>
        /// Compares (Start, End, Hash) only; nodes sharing this prefix form a contiguous in-order group.
        /// </summary>
        private static int ComparePrefix(DateTimeOffset s1, DateTimeOffset e1, int h1, Node n)
        {
            int c = s1.CompareTo(n.Start);
            if (c != 0) return c;
            c = e1.CompareTo(n.End);
            if (c != 0) return c;
            return h1.CompareTo(n.Hash);
        }

        /// <summary>
        /// Total order over (Start, End, Hash, Seq). Seq is unique, so distinct values
        /// with colliding hashes never compare equal.
        /// </summary>
        private static int CompareKey(DateTimeOffset s1, DateTimeOffset e1, int h1, long q1, Node n)
        {
            int c = ComparePrefix(s1, e1, h1, n);
            return c != 0 ? c : q1.CompareTo(n.Seq);
        }

        /// <summary>
        /// Finds a node with exactly (start, end, value), scanning the group of nodes sharing the same hash.
        /// </summary>
        private static Node? FindNode(Node? node, DateTimeOffset start, DateTimeOffset end, int hash, T value)
        {
            while (node is not null)
            {
                int c = ComparePrefix(start, end, hash, node);
                if (c < 0) { node = node.Left; continue; }
                if (c > 0) { node = node.Right; continue; }

                if (EqualityComparer<T>.Default.Equals(value, node.Value))
                    return node;

                return FindNode(node.Left, start, end, hash, value)
                    ?? FindNode(node.Right, start, end, hash, value);
            }
            return null;
        }

        /// <summary>
        /// Recomputes MaxEnd from children and own End.
        /// </summary>
        private static void Update(Node n)
        {
            var max = n.End;
            if (n.Left is { } l && l.MaxEnd > max) max = l.MaxEnd;
            if (n.Right is { } r && r.MaxEnd > max) max = r.MaxEnd;
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
        private Node InsertTreap(Node? node, DateTimeOffset start, DateTimeOffset end, T value, int hash, long seq)
        {
            if (node is null)
                return new Node(start, end, value, hash, seq, _rng.Next());

            // Seq is unique, so cmp is never 0.
            int cmp = CompareKey(start, end, hash, seq, node);

            if (cmp < 0)
            {
                node.Left = InsertTreap(node.Left, start, end, value, hash, seq);
                if (node.Left!.Priority < node.Priority)
                    node = RotateRight(node);
            }
            else
            {
                node.Right = InsertTreap(node.Right, start, end, value, hash, seq);
                if (node.Right!.Priority < node.Priority)
                    node = RotateLeft(node);
            }

            Update(node);
            return node;
        }

        /// <summary>
        /// Removes a node matching (start, end, value) using treap deletion:
        /// rotate the node down according to child priorities until it becomes a leaf, then drop it.
        /// </summary>
        private static (Node? node, bool removed) RemoveTreap(Node? node, DateTimeOffset start, DateTimeOffset end, int hash, long seq)
        {
            if (node is null) return (null, false);

            int cmp = CompareKey(start, end, hash, seq, node);

            bool removed;
            if (cmp < 0)
            {
                (node.Left, removed) = RemoveTreap(node.Left, start, end, hash, seq);
            }
            else if (cmp > 0)
            {
                (node.Right, removed) = RemoveTreap(node.Right, start, end, hash, seq);
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
                // Children are already fully pruned by the recursive calls above,
                // so no descendant has End < cutoff. DeleteRoot promotes a clean
                // node, hence no extra recursion is required here.
                return DeleteRoot(node);
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
                // Children are already pruned, so the promoted replacement is clean.
                return DeleteRoot(node);
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

            // Left subtree can overlap only if its MaxEnd >= qs.
            if (node.Left is not null && node.Left.MaxEnd >= qs)
                QueryCollect(node.Left, qs, qe, result);

            // Current node overlaps if Start <= qe && End >= qs.
            if (node.Start <= qe && node.End >= qs)
                result.Add(new TemporalItem<T>(node.Value, node.Start));

            // The right subtree only contains starts >= node.Start. If node.Start
            // already exceeds qe, every right descendant exceeds qe as well and
            // cannot overlap [qs, qe], so we can safely prune it.
            if (node.Right is not null && node.Start <= qe)
                QueryCollect(node.Right, qs, qe, result);
        }

        /// <summary>
        /// Counts intervals overlapping [qs, qe] using the same pruning as <see cref="QueryCollect"/>.
        /// </summary>
        private static int CountOverlap(Node? node, DateTimeOffset qs, DateTimeOffset qe)
        {
            if (node is null) return 0;

            int count = 0;
            if (node.Left is not null && node.Left.MaxEnd >= qs)
                count += CountOverlap(node.Left, qs, qe);

            if (node.Start <= qe && node.End >= qs)
                count++;

            if (node.Right is not null && node.Start <= qe)
                count += CountOverlap(node.Right, qs, qe);

            return count;
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

            // Same right-subtree pruning as in QueryCollect.
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