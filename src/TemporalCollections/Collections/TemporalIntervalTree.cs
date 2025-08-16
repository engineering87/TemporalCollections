// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;
using TemporalCollections.Utilities;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe interval tree for storing temporal intervals with associated values.
    /// Supports insertion, removal, querying of overlapping intervals and time-based operations via <see cref="ITimeQueryable{T}"/>.
    /// Public API uses DateTime; internal timeline is DateTimeOffset (UTC).
    /// </summary>
    /// <typeparam name="T">The type of the value associated with each interval.</typeparam>
    public class TemporalIntervalTree<T> : ITimeQueryable<T>
    {
        // Centralized policy for DateTimeKind.Unspecified handling.
        private const UnspecifiedPolicy DefaultPolicy = UnspecifiedPolicy.AssumeUtc;

        /// <summary>
        /// Represents a single node in the interval tree.
        /// </summary>
        private class Node
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
            public T Value;

            /// <summary>
            /// The maximum End of this node or any of its descendants (UTC).
            /// </summary>
            public DateTimeOffset MaxEnd;

            public Node? Left;
            public Node? Right;

            public Node(DateTimeOffset start, DateTimeOffset end, T value)
            {
                if (end < start)
                    throw new ArgumentException("End must be >= Start", nameof(end));

                Start = start;
                End = end;
                Value = value;
                MaxEnd = end;
            }
        }

        private Node? _root;
        private readonly Lock _lock = new();

        /// <summary>
        /// Inserts a new interval with an associated value into the tree.
        /// </summary>
        public void Insert(DateTime start, DateTime end, T value)
        {
            // Normalize to UTC offsets
            var s = TimeNormalization.ToUtcOffset(start, nameof(start), DefaultPolicy);
            var e = TimeNormalization.ToUtcOffset(end, nameof(end), DefaultPolicy);
            if (e < s) throw new ArgumentException("end must be >= start", nameof(end));

            lock (_lock)
            {
                _root = Insert(_root, s, e, value);
            }
        }

        /// <summary>
        /// Removes an interval with the exact same start, end, and value from the tree.
        /// </summary>
        public bool Remove(DateTime start, DateTime end, T value)
        {
            var s = TimeNormalization.ToUtcOffset(start, nameof(start), DefaultPolicy);
            var e = TimeNormalization.ToUtcOffset(end, nameof(end), DefaultPolicy);

            lock (_lock)
            {
                bool removed;
                (_root, removed) = Remove(_root, s, e, value);
                return removed;
            }
        }

        /// <summary>
        /// Returns all values whose intervals overlap with the given query range.
        /// The returned items are wrapped as <see cref="TemporalItem{T}"/> where Timestamp equals interval Start.
        /// </summary>
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime queryStart, DateTime queryEnd)
        {
            var (qs, qe) = TimeNormalization.NormalizeRange(queryStart, queryEnd, nameof(queryStart), nameof(queryEnd), DefaultPolicy);

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
        public void RemoveOlderThan(DateTime cutoff)
        {
            var c = TimeNormalization.ToUtcOffset(cutoff, nameof(cutoff), DefaultPolicy);

            lock (_lock)
            {
                _root = RemoveOlderThanInternal(_root, c);
            }
        }

        /// <summary>
        /// Returns all values whose intervals overlap with the given query range (values only).
        /// </summary>
        public IList<T> Query(DateTime queryStart, DateTime queryEnd)
        {
            var (qs, qe) = TimeNormalization.NormalizeRange(queryStart, queryEnd, nameof(queryStart), nameof(queryEnd), DefaultPolicy);

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
        public TimeSpan GetTimeSpan()
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
        public int CountInRange(DateTime from, DateTime to)
        {
            var (f, t) = TimeNormalization.NormalizeRange(from, to, nameof(from), nameof(to), DefaultPolicy);

            lock (_lock)
            {
                return CountByStartRange(_root, f, t);
            }
        }

        /// <summary>
        /// Removes all items whose timestamps (Start) fall within [from, to] (inclusive).
        /// </summary>
        public void RemoveRange(DateTime from, DateTime to)
        {
            var (f, t) = TimeNormalization.NormalizeRange(from, to, nameof(from), nameof(to), DefaultPolicy);

            lock (_lock)
            {
                _root = RemoveByStartRange(_root, f, t);
            }
        }

        /// <summary>
        /// Removes all items from the collection.
        /// </summary>
        public void Clear()
        {
            lock (_lock) { _root = null; }
        }

        /// <summary>
        /// Retrieves the latest item by timestamp (max Start) or null if empty.
        /// </summary>
        public TemporalItem<T>? GetLatest()
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
        public TemporalItem<T>? GetEarliest()
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
        public IEnumerable<TemporalItem<T>> GetBefore(DateTime time)
        {
            var cutoff = TimeNormalization.ToUtcOffset(time, nameof(time), DefaultPolicy);

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
        public IEnumerable<TemporalItem<T>> GetAfter(DateTime time)
        {
            var cutoff = TimeNormalization.ToUtcOffset(time, nameof(time), DefaultPolicy);

            lock (_lock)
            {
                var list = new List<TemporalItem<T>>();
                CollectAfter(_root, cutoff, list);
                return list;
            }
        }

        #region Internal helpers (UTC DateTimeOffset)

        /// <summary>
        /// Collect TemporalItem<T> for intervals overlapping [qs, qe].
        /// </summary>
        /// <param name="node"></param>
        /// <param name="qs"></param>
        /// <param name="qe"></param>
        /// <param name="result"></param>
        private static void QueryCollect(Node? node, DateTimeOffset qs, DateTimeOffset qe, List<TemporalItem<T>> result)
        {
            if (node is null) return;

            // left subtree can overlap only if its MaxEnd >= qs
            if (node.Left is not null && node.Left.MaxEnd >= qs)
                QueryCollect(node.Left, qs, qe, result);

            // current node overlaps if Start <= qe && End >= qs
            if (node.Start <= qe && node.End >= qs)
                result.Add(new TemporalItem<T>(node.Value, node.Start));

            // right subtree may have starts <= qe
            if (node.Right is not null && node.Start <= qe)
                QueryCollect(node.Right, qs, qe, result);
        }

        /// <summary>
        /// Collect values for compatibility with "Query" method.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="qs"></param>
        /// <param name="qe"></param>
        /// <param name="result"></param>
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

        private static Node Insert(Node? node, DateTimeOffset start, DateTimeOffset end, T value)
        {
            if (node == null)
                return new Node(start, end, value);

            if (start < node.Start)
                node.Left = Insert(node.Left, start, end, value);
            else
                node.Right = Insert(node.Right, start, end, value);

            node.MaxEnd = MaxDate(node.End,
                node.Left?.MaxEnd ?? DateTimeOffset.MinValue,
                node.Right?.MaxEnd ?? DateTimeOffset.MinValue);

            return node;
        }

        private static (Node? node, bool removed) Remove(Node? node, DateTimeOffset start, DateTimeOffset end, T value)
        {
            if (node == null) return (null, false);

            bool removed = false;

            if (start == node.Start && end == node.End && EqualityComparer<T>.Default.Equals(value, node.Value))
            {
                removed = true;

                // Node with at most one child
                if (node.Left == null) return (node.Right, true);
                if (node.Right == null) return (node.Left, true);

                // Two children: replace with inorder successor
                var minNode = FindMin(node.Right);
                node.Start = minNode.Start;
                node.End = minNode.End;
                node.Value = minNode.Value;
                node.Right = RemoveMin(node.Right);
            }
            else if (start < node.Start)
            {
                (node.Left, removed) = Remove(node.Left, start, end, value);
            }
            else
            {
                (node.Right, removed) = Remove(node.Right, start, end, value);
            }

            if (node != null)
            {
                node.MaxEnd = MaxDate(node.End,
                    node.Left?.MaxEnd ?? DateTimeOffset.MinValue,
                    node.Right?.MaxEnd ?? DateTimeOffset.MinValue);
            }

            return (node, removed: false);
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
        /// Removes nodes whose End < cutoff and returns new subtree root.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="cutoff"></param>
        /// <returns></returns>
        private static Node? RemoveOlderThanInternal(Node? node, DateTimeOffset cutoff)
        {
            if (node == null) return null;

            node.Left = RemoveOlderThanInternal(node.Left, cutoff);
            node.Right = RemoveOlderThanInternal(node.Right, cutoff);

            if (node.End < cutoff)
            {
                // delete this node
                if (node.Left == null) return node.Right;
                if (node.Right == null) return node.Left;

                var minNode = FindMin(node.Right);
                node.Start = minNode.Start;
                node.End = minNode.End;
                node.Value = minNode.Value;
                node.Right = RemoveMin(node.Right);
            }

            node.MaxEnd = MaxDate(node.End,
                node.Left?.MaxEnd ?? DateTimeOffset.MinValue,
                node.Right?.MaxEnd ?? DateTimeOffset.MinValue);

            return node;
        }

        private static Node FindMin(Node node)
        {
            while (node.Left != null) node = node.Left;
            return node;
        }

        private static Node? RemoveMin(Node node)
        {
            if (node.Left == null)
                return node.Right;

            node.Left = RemoveMin(node.Left);

            node.MaxEnd = MaxDate(node.End,
                node.Left?.MaxEnd ?? DateTimeOffset.MinValue,
                node.Right?.MaxEnd ?? DateTimeOffset.MinValue);

            return node;
        }

        /// <summary>
        /// Delete a single node and return the new subtree root.
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        private static Node? DeleteNode(Node node)
        {
            if (node.Left is null) return UpdateMaxEndChain(node.Right);
            if (node.Right is null) return UpdateMaxEndChain(node.Left);

            var succ = FindMin(node.Right);
            node.Start = succ.Start;
            node.End = succ.End;
            node.Value = succ.Value;
            node.Right = RemoveMin(node.Right);

            return UpdateMaxEndChain(node);
        }

        /// <summary>
        /// Removes nodes whose Start is in [from, to] (inclusive) and returns new subtree root.
        /// </summary>
        private static Node? RemoveByStartRange(Node? node, DateTimeOffset from, DateTimeOffset to)
        {
            if (node is null) return null;

            // prune children first
            node.Left = RemoveByStartRange(node.Left, from, to);
            node.Right = RemoveByStartRange(node.Right, from, to);

            // handle current node
            if (node.Start >= from && node.Start <= to)
            {
                node = DeleteNode(node);
                // re-run on new root in case replacement also lies in range
                node = RemoveByStartRange(node, from, to);
                return node; // Update done in DeleteNode / recursive calls
            }

            return UpdateMaxEndChain(node);
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

        private static DateTimeOffset MaxDate(params DateTimeOffset[] dates)
        {
            DateTimeOffset max = DateTimeOffset.MinValue;
            foreach (var d in dates)
                if (d > max) max = d;
            return max;
        }

        /// <summary>
        /// Recomputes MaxEnd along the current node and returns it.
        /// </summary>
        private static Node? UpdateMaxEndChain(Node? node)
        {
            if (node is null) return null;
            node.MaxEnd = MaxDate(node.End,
                node.Left?.MaxEnd ?? DateTimeOffset.MinValue,
                node.Right?.MaxEnd ?? DateTimeOffset.MinValue);
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

        #endregion
    }
}