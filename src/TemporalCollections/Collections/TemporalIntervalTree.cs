// (c) 2025 Francesco Del Re <francesco.delre.87@gmail.com>
// This code is licensed under MIT license (see LICENSE.txt for details)
using TemporalCollections.Abstractions;
using TemporalCollections.Models;

namespace TemporalCollections.Collections
{
    /// <summary>
    /// A thread-safe interval tree for storing temporal intervals with associated values.
    /// Supports insertion, removal, querying of overlapping intervals and time-based operations via <see cref="ITimeQueryable{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type of the value associated with each interval.</typeparam>
    public class TemporalIntervalTree<T> : ITimeQueryable<T>
    {
        /// <summary>
        /// Represents a single node in the interval tree.
        /// </summary>
        private class Node
        {
            /// <summary>Interval start (inclusive).</summary>
            public DateTime Start;

            /// <summary>Interval end (inclusive).</summary>
            public DateTime End;

            /// <summary>Associated value for the interval.</summary>
            public T Value;

            /// <summary>The maximum end time of this node or any of its descendants.</summary>
            public DateTime MaxEnd;

            public Node? Left;
            public Node? Right;

            /// <summary>
            /// Initializes a new instance of the <see cref="Node"/> class.
            /// </summary>
            /// <param name="start">The start time of the interval.</param>
            /// <param name="end">The end time of the interval (must be >= start).</param>
            /// <param name="value">The value associated with the interval.</param>
            public Node(DateTime start, DateTime end, T value)
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
        /// <param name="start">The start time of the interval.</param>
        /// <param name="end">The end time of the interval (must be >= start).</param>
        /// <param name="value">The value to associate with the interval.</param>
        /// <exception cref="ArgumentException">Thrown when <paramref name="end"/> is earlier than <paramref name="start"/>.</exception>
        public void Insert(DateTime start, DateTime end, T value)
        {
            if (end < start)
                throw new ArgumentException("end must be >= start", nameof(end));

            lock (_lock)
            {
                _root = Insert(_root, start, end, value);
            }
        }

        /// <summary>
        /// Removes an interval with the exact same start, end, and value from the tree.
        /// </summary>
        /// <param name="start">The start time of the interval.</param>
        /// <param name="end">The end time of the interval.</param>
        /// <param name="value">The value associated with the interval.</param>
        /// <returns>True if the interval was found and removed; otherwise false.</returns>
        public bool Remove(DateTime start, DateTime end, T value)
        {
            lock (_lock)
            {
                bool removed;
                (_root, removed) = Remove(_root, start, end, value);
                return removed;
            }
        }

        /// <summary>
        /// Returns all values whose intervals overlap with the given query range.
        /// The returned items are wrapped as <see cref="TemporalItem{T}"/> where the item.Timestamp equals the interval Start.
        /// </summary>
        /// <param name="queryStart">The start time of the query interval.</param>
        /// <param name="queryEnd">The end time of the query interval (must be >= queryStart).</param>
        /// <returns>A list of temporal items whose intervals overlap the query interval.</returns>
        /// <exception cref="ArgumentException">Thrown when <paramref name="queryEnd"/> is earlier than <paramref name="queryStart"/>.</exception>
        public IEnumerable<TemporalItem<T>> GetInRange(DateTime queryStart, DateTime queryEnd)
        {
            if (queryEnd < queryStart)
                throw new ArgumentException("queryEnd must be >= queryStart", nameof(queryEnd));

            lock (_lock)
            {
                var result = new List<TemporalItem<T>>();
                QueryCollect(_root, queryStart, queryEnd, result);
                return result;
            }
        }

        /// <summary>
        /// Removes all intervals that have already ended strictly before the cutoff (i.e. End &lt; cutoff).
        /// </summary>
        /// <param name="cutoff">The cutoff timestamp; intervals with End &lt; cutoff will be removed.</param>
        public void RemoveOlderThan(DateTime cutoff)
        {
            lock (_lock)
            {
                _root = RemoveOlderThanInternal(_root, cutoff);
            }
        }

        /// <summary>
        /// Returns all values whose intervals overlap with the given query range.
        /// This method keeps the original list-oriented API (returns values only) — useful alongside QueryCollect / direct use.
        /// </summary>
        /// <param name="queryStart">The start of the query interval.</param>
        /// <param name="queryEnd">The end of the query interval.</param>
        /// <returns>A list of values overlapping the query interval.</returns>
        /// <remarks>
        /// This helper retains the original "Query" behavior present in earlier versions.
        /// </remarks>
        public IList<T> Query(DateTime queryStart, DateTime queryEnd)
        {
            if (queryEnd < queryStart)
                throw new ArgumentException("queryEnd must be >= queryStart", nameof(queryEnd));

            lock (_lock)
            {
                var result = new List<T>();
                QueryValues(_root, queryStart, queryEnd, result);
                return result;
            }
        }

        /// <summary>
        /// Returns the total timespan covered by items in the collection
        /// as the difference between the earliest and latest timestamps (timestamps == interval starts).
        /// Returns <see cref="TimeSpan.Zero"/> if the tree is empty or contains a single item.
        /// </summary>
        public TimeSpan GetTimeSpan()
        {
            lock (_lock)
            {
                if (_root is null) return TimeSpan.Zero;

                var minNode = FindMinByStart(_root);
                var maxNode = FindMaxByStart(_root);
                if (minNode is null || maxNode is null) return TimeSpan.Zero;

                var span = maxNode.Start - minNode.Start;
                return span < TimeSpan.Zero ? TimeSpan.Zero : span;
            }
        }

        /// <summary>
        /// Returns the number of items whose timestamps fall within [from, to] (inclusive).
        /// Here, the timestamp is the interval start.
        /// </summary>
        /// <param name="from">Range start (inclusive).</param>
        /// <param name="to">Range end (inclusive).</param>
        public int CountInRange(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("to must be >= from", nameof(to));

            lock (_lock)
            {
                return CountByStartRange(_root, from, to);
            }
        }

        /// <summary>
        /// Removes all items whose timestamps fall within the specified range [from, to] (inclusive).
        /// Timestamp is the interval start.
        /// </summary>
        /// <param name="from">Range start (inclusive).</param>
        /// <param name="to">Range end (inclusive).</param>
        public void RemoveRange(DateTime from, DateTime to)
        {
            if (to < from)
                throw new ArgumentException("to must be >= from", nameof(to));

            lock (_lock)
            {
                _root = RemoveByStartRange(_root, from, to);
            }
        }

        /// <summary>
        /// Removes all items from the collection.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _root = null;
            }
        }

        /// <summary>
        /// Retrieves the latest item based on timestamp (maximum Start) or null if empty.
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
        /// Retrieves the earliest item based on timestamp (minimum Start) or null if empty.
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
        /// <param name="time">Exclusive upper bound for the timestamp.</param>
        public IEnumerable<TemporalItem<T>> GetBefore(DateTime time)
        {
            lock (_lock)
            {
                var list = new List<TemporalItem<T>>();
                CollectBefore(_root, time, list);
                return list;
            }
        }

        /// <summary>
        /// Retrieves all items with timestamp strictly after the specified time (Start &gt; time).
        /// </summary>
        /// <param name="time">Exclusive lower bound for the timestamp.</param>
        public IEnumerable<TemporalItem<T>> GetAfter(DateTime time)
        {
            lock (_lock)
            {
                var list = new List<TemporalItem<T>>();
                CollectAfter(_root, time, list);
                return list;
            }
        }

        #region Internal helpers

        // Collect TemporalItem<T> for intervals overlapping [qs,qe].
        private static void QueryCollect(Node? node, DateTime qs, DateTime qe, List<TemporalItem<T>> result)
        {
            if (node is null) return;

            // Left subtree: only if it may overlap [qs, qe]
            if (node.Left is not null && node.Left.MaxEnd >= qs)
                QueryCollect(node.Left, qs, qe, result);

            // Current node
            if (node.Start <= qe && node.End >= qs)
                result.Add(new TemporalItem<T>(node.Value, node.Start));

            // Right subtree: only if nodes there can start <= qe
            if (node.Right is not null && node.Start <= qe)
                QueryCollect(node.Right, qs, qe, result);
        }

        // Collect values for compatibility with older Query method
        private static void QueryValues(Node? node, DateTime qs, DateTime qe, List<T> result)
        {
            if (node is null) return;

            if (node.Left is not null && node.Left.MaxEnd >= qs)
                QueryValues(node.Left, qs, qe, result);

            if (node.Start <= qe && node.End >= qs)
                result.Add(node.Value);

            if (node.Right is not null && node.Start <= qe)
                QueryValues(node.Right, qs, qe, result);
        }

        private static Node Insert(Node? node, DateTime start, DateTime end, T value)
        {
            if (node == null)
                return new Node(start, end, value);

            if (start < node.Start)
                node.Left = Insert(node.Left, start, end, value);
            else
                node.Right = Insert(node.Right, start, end, value);

            node.MaxEnd = MaxDate(node.End,
                node.Left?.MaxEnd ?? DateTime.MinValue,
                node.Right?.MaxEnd ?? DateTime.MinValue);

            return node;
        }

        private static (Node? node, bool removed) Remove(Node? node, DateTime start, DateTime end, T value)
        {
            if (node == null) return (null, false);

            bool removed = false;

            if (start == node.Start && end == node.End && EqualityComparer<T>.Default.Equals(value, node.Value))
            {
                removed = true;

                // Node with at most one child
                if (node.Left == null) return (node.Right, true);
                if (node.Right == null) return (node.Left, true);

                // Node with two children: replace with inorder successor
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
                    node.Left?.MaxEnd ?? DateTime.MinValue,
                    node.Right?.MaxEnd ?? DateTime.MinValue);
            }

            return (node, removed);
        }

        /// <summary>
        /// Counts nodes with Start in [from, to] using BST pruning.
        /// </summary>
        private static int CountByStartRange(Node? node, DateTime from, DateTime to)
        {
            if (node is null) return 0;

            if (node.Start < from)
            {
                return CountByStartRange(node.Right, from, to);
            }
            if (node.Start > to)
            {
                return CountByStartRange(node.Left, from, to);
            }

            // node.Start within [from, to]
            return 1 + CountByStartRange(node.Left, from, to) + CountByStartRange(node.Right, from, to);
        }


        // Removes nodes whose End < cutoff and returns new subtree root.
        private static Node? RemoveOlderThanInternal(Node? node, DateTime cutoff)
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
                node.Left?.MaxEnd ?? DateTime.MinValue,
                node.Right?.MaxEnd ?? DateTime.MinValue);

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
                node.Left?.MaxEnd ?? DateTime.MinValue,
                node.Right?.MaxEnd ?? DateTime.MinValue);

            return node;
        }

        // Delete a single node and return the new subtree root.
        // - 0/1 child: return the non-null child (or null)
        // - 2 children: replace with inorder successor (min on right) and remove that successor
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
        /// Removes nodes whose Start is in [from, to] (inclusive) and returns the new subtree root.
        /// </summary>
        private static Node? RemoveByStartRange(Node? node, DateTime from, DateTime to)
        {
            if (node is null) return null;

            // First prune children so we don't miss any matches deeper in the tree.
            node.Left = RemoveByStartRange(node.Left, from, to);
            node.Right = RemoveByStartRange(node.Right, from, to);

            // Now handle the current node. If it's in range, delete it and re-run on the new root.
            if (node.Start >= from && node.Start <= to)
            {
                node = DeleteNode(node);

                // The subtree root changed; keep removing while the new root is still in range.
                node = RemoveByStartRange(node, from, to);
                return node; // UpdateMaxEndChain already done in DeleteNode + recursive calls
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

        private static DateTime MaxDate(params DateTime[] dates)
        {
            DateTime max = DateTime.MinValue;
            foreach (var d in dates)
            {
                if (d > max) max = d;
            }
            return max;
        }

        /// <summary>
        /// Recomputes MaxEnd along the current node and returns it.
        /// </summary>
        private static Node? UpdateMaxEndChain(Node? node)
        {
            if (node is null) return null;
            node.MaxEnd = MaxDate(node.End,
                node.Left?.MaxEnd ?? DateTime.MinValue,
                node.Right?.MaxEnd ?? DateTime.MinValue);
            return node;
        }

        /// <summary>
        /// In-order traversal collecting nodes with Start &lt; time; prunes right branch when possible.
        /// </summary>
        private static void CollectBefore(Node? node, DateTime time, List<TemporalItem<T>> acc)
        {
            if (node is null) return;

            if (node.Start >= time)
            {
                // Entire right subtree also has Start >= time if we came from left? Not guaranteed;
                // But we can safely skip the right subtree only when node.Start >= time and right.Start >= node.Start.
                // We still need to explore left where smaller Starts may exist.
                CollectBefore(node.Left, time, acc);
                return;
            }

            // node.Start < time
            CollectBefore(node.Left, time, acc);
            acc.Add(new TemporalItem<T>(node.Value, node.Start));
            CollectBefore(node.Right, time, acc);
        }

        /// <summary>
        /// In-order traversal collecting nodes with Start &gt; time; prunes left branch when possible.
        /// </summary>
        private static void CollectAfter(Node? node, DateTime time, List<TemporalItem<T>> acc)
        {
            if (node is null) return;

            if (node.Start <= time)
            {
                // All in left subtree are <= node.Start <= time, so skip left; check right.
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